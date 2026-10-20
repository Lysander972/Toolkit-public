# 工程文件基础设施

## Purpose

工程文件层唯一服务实体抽象层，提供原始字节缓存、基础 IO 与文件监听事件源。实体层负责工程设置和资产解析、文件集合、期望状态、Diff、操作顺序和业务一致性。Host 仅装配文件服务，工程状态经实体层查询。

## Requirements

### Requirement: 工程根路径与原生文件语义

内部路径 MUST 为工程根相对路径，分隔符为 `/`，根目录为 `.`。绝对路径、盘符、反斜杠、空路径、空段、点段与 NUL MUST 拒绝，不自动修复。绝对根目录仅由文件系统边界持有。原生解析结果 MUST 位于工程根内；现有目标解析自身，创建目标解析父目录。路径解析期间的权限、缺失父目录和其他原生错误直接失败。

相对路径合同 MUST 由 Project Model 的 WorkspacePaths 唯一声明，实体声明、名称操作与 Host 文件系统边界复用同一校验；原生 realPath 与实际 IO 仍由 Host 持有。

元数据 MUST 返回相对路径、文件或目录类型、大小和原生版本。版本使用 Node stat 的 dev、ino、mtimeNs、ctimeNs 和大小，仅作为缓存复核依据，不建立物理身份注册表。目录 MUST 直接枚举并按相对路径排序，每次查询实际条目。链接遵循原生解析，硬链接按名称保留，工程根外的目标失败。工程根不能被写入或删除。

必要性：Node path 拼接不规定项目输入形式，realPath 提供链接解析，Node stat 提供纳秒版本；项目只负责相对路径合同和工程根边界，不实现路径兼容、Windows 私有共享模式或实体筛选。真实目录、硬链接、工程内外链接和非法输入验证边界。

备选与取舍：直接拼接路径无法校验链接的实际目标；自研物理身份注册表则重复原生文件系统状态。采用 WorkspacePaths 校验输入、realPath 校验目标与 stat 查询版本，每次请求增加必要的元数据 IO，换取实际链接边界和外部修改可见性；硬链接仍按工程名称分别交付。这是本地文件服务的最小职责。

#### Scenario: 工程路径与目录枚举

- **WHEN** 实体层请求路径、目录条目或链接目标
- **THEN** 返回实际文件元数据与相对路径，硬链接不归并；非法输入及工程根外目标失败，目录变化在下次枚举立即可见

<!-- @anchor id=workspace-file-paths -->
<!-- @architecture id=toolkit.project_model.logic -->
<!-- @architecture id=toolkit.files -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=workspace-file-paths level=L -->

### Requirement: 按需有界原始字节缓存

原始字节 MUST 仅由文件服务缓存。容量及淘汰 MUST 使用 Effect Cache：128 项，单项最多 1 MiB，超过预算完整读取且不缓存。不缓存目录列表，不建立全工程预读或另一份失效索引。字节缓存不另外启动监听；同一文件层的 ProjectWatcher 提供唯一 Chokidar 事件源，其作用域、原生通知与归并合同见实体规范。文件服务不解码文本、不规范化换行、不计算业务摘要。

每次读取 MUST 先获取原生版本，按相对路径与该版本访问缓存，再复核版本。稳定输入的并发读取 MUST 合并实际加载；读取期间变化 MUST 失效该次缓存并失败，不自动重试。失败加载不持久缓存，交付字节为独立副本。本地写入和外部修改通过后续版本查询选择新的缓存项；已删除文件的读取自然失败。

必要性：Effect Cache 原生提供容量、并发加载合并、等价键和失败 TTL。项目只负责路径与原生元数据键、读取前后复核和字节副本，不自造缓存算法。元数据复核不宣称抵御刻意维持所有原生版本字段的修改，也不提供跨文件快照。真实读写、等长修改、读取中变化、加载失败、大文件和容量淘汰验证行为。

备选与取舍：每次直接读取最简单，但重复消费同一版本会重复加载；全工程预读或自行维护淘汰状态会增加内存和失效责任。复用 Effect Cache，保留读取前后版本查询和交付副本，代价是元数据 IO 与字节复制；128 项及每项 1 MiB 将可缓存正文限制在约 128 MiB，大文件仍完整交付。文件层只复用原始字节，语义快照由实体层形成。

#### Scenario: 内容复用与版本复核

- **WHEN** 并发读取稳定文件，修改文件，或读取期间发生变化
- **THEN** 稳定读取合并，返回副本不污染缓存；原生版本变化重新加载，读取中变化和实际 IO 错误直接失败

<!-- @anchor id=workspace-file-cache -->
<!-- @architecture id=toolkit.files -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=workspace-file-cache level=M -->

### Requirement: 实体层编排四类文件原语

文件层 MUST 只提供写文件、删文件、建目录和删空目录四类变更。实体层决定每项路径和内容、计算 Diff 并安排操作顺序；文件层不提供移动、写入权 Scope、摘要冲突协议、批量计划或事务。以单个 Host 的文件操作为场景，不建立多个 Host 争抢工程的锁、重试和协调状态。

写文件 MUST 使用 write-file-atomic 单文件原子替换；调用输入复制为独立字节。写入前仅对已存在、大小相同的文件比较实际字节；相同内容返回 false 且零写入，新建或变化内容成功后返回 true。只有实际 ENOENT 表达新建，权限和其他错误直接传播。文本编码、BOM 与换行由实体层处理，文件层原样保存字节。建目录不递归，删文件不删除目录，删目录只删除空目录；缺失、类型不匹配和权限错误直接失败。某项失败保留之前已完成的磁盘结果。

必要性：Effect FileSystem 提供成熟读写与目录能力，write-file-atomic 负责临时文件、同步和原子替换，Node unlink/rmdir 提供单文件与空目录删除。项目只补充同内容零写入和工程边界，不实现写队列、跨进程锁、跨文件事务或回滚。真实原子写入、四类 IO 和失败后的实际磁盘结果验证行为。

备选与取舍：直接覆盖文件会在写入中断时暴露部分内容；将批量计划或事务放入文件层会重复实体层的候选与操作顺序。采用成熟单文件原子替换及原生删除、建目录能力，同大小文件增加一次字节比较以实现零写入。多文件失败保留实际完成结果，实体层据此重读；这与本地单 Host 的控制权一致。

#### Scenario: 四类原语与同内容零写入

- **WHEN** 实体层依次提交文件或目录操作
- **THEN** 同内容不落盘，变化内容原子替换；四类原语执行实际 IO，每项返回实际结果，失败自然暴露且保留已完成操作

<!-- @anchor id=workspace-file-writes -->
<!-- @architecture id=toolkit.files -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=workspace-file-writes level=M -->

### Requirement: 行为测试资源

测试 MUST 使用仓库顶层 `_build/tests` 下的独占小型工程，不修改原始夹具。成功和失败路径均释放自有夹具，不把 Mock 调用代替实际文件行为。

FileTestWorkspace.make MUST 一次创建独占目录与真实 ProjectFiles，并以只读成员交付；装配失败释放本次目录。测试主动持有资源、执行操作，并在 finally 调用 close。资源对象只负责自有目录与实际写入统计，不接收业务回调。必要性：Node mkdtemp 提供目录隔离，既有文件 Layer 提供真实 IO；项目仅连接两者并统计实际写入。原生 try/finally 负责测试控制权，独立实例与失败释放通过实际文件状态验证。

#### Scenario: 独占夹具与资源释放

- **WHEN** 执行文件基础设施行为测试
- **THEN** 验证实际文件行为，测试结束释放自有资源

<!-- @anchor id=workspace-file-test-resources -->
<!-- @architecture id=toolkit.host.test_support -->
<!-- @architecture id=toolkit.host.tests -->
<!-- @budget id=workspace-file-test-resources level=M -->

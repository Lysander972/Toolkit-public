# Host Configuration Specification

## Purpose

约束保留的 C# Host 与资产生成模块的 TOML 配置、密钥编码和默认值。现役 VS Code 前端采用 WorkspaceConfiguration，其合同见 `vscode-frontend`，不参与此文件的读写或同步。C# Host 源码与测试保持参考归属，不因文档迁移转为现役模块。

## Requirements

### Requirement: Host 配置唯一来源

保留的 Host 配置 MUST 唯一位于 `%USERPROFILE%/AppData/LocalLow/HYH/小玩法/toolkit.config.toml`，密钥 MUST 位于同目录 `toolkit.key`，MUST NOT 写入配置正文或仓库。TOML 条目 MUST 使用全小写 snake_case 点分名称。Host 与资产模块 MUST NOT 采用环境变量、私有副本或别名作为第二来源。文件操作测试 MUST 经构造函数注入独占目录。

Host 配置默认值如下；只属于前端的 lint、Canvas 尺寸和 CodeLens 设置不属于该合同。

| 条目 | 类型 | 默认值 |
|---|---|---|
| `mcp.port` | int | 5050 |
| `browser.render_worker_count` | int | 2 |
| `browser.executable_path` | string | 空串 |
| `resource_gen.comfyui.base_url` | string | 空串 |
| `resource_gen.comfyui.api_key` | string | 空串 |
| `resource_gen.comfyui.tripo_api_key` | string | 空串 |

ComfyUI URL MUST 指向远端工作流应用实例，MUST NOT 指向本地推理实例；URL 中密钥 MUST 通过 `{api_key}` 占位符引用，MUST NOT 包含明文。模型条目为 `resource_gen.comfyui.model.<域>`，默认值如下。

| 域 | 默认模型 |
|---|---|
| `text_to_image` | 千问2.0-文生图 |
| `image_to_image` | 千问2.0-图像编辑 |
| `image_to_video` | 万相2.7-图生视频 |
| `text_to_video` | 万相2.5 Preview 文生视频 |
| `text_to_music` | suno-single-v5.5 |
| `image_to_3d` | hitem3d-v15/image-to-3d |

模型选择 MUST 限于 RH 收藏清单内创建时间不超过六个月的模型，收藏与查询合同见 `docs/ComfyUI_App_Platforms_Index.md`。

#### Scenario: 保留模块读取配置

- **WHEN** Host 或资产模块读取用户配置
- **THEN** 使用唯一 TOML 文件和规范条目，不消费 VS Code 设置副本或环境变量

<!-- @anchor id=host-configuration-source -->
<!-- @nonCode -->

### Requirement: 密钥编码与原子持久化

非空密钥条目 MUST 经 AES-256-GCM 加密，文本形态 MUST 为 `enc:v1:` 加 `base64(nonce ‖ tag ‖ ciphertext)`；明文编码为 UTF-8，nonce 为 12 字节，tag 为 16 字节。空串表示未配置，MUST NOT 加密。密钥文件 MUST 为无头、无编码的 32 字节随机值，首次使用生成；Windows 限当前用户 ACL，Linux 权限为 0600。

配置文件缺失时 MUST 按默认值物化，写入 MUST 原子替换；并发创建 MUST 遵循文件系统原生排他创建语义。密钥解密与加密 MUST 经过同一加密层，MUST NOT 将明文写入配置或日志。

#### Scenario: 加解密字节序与配置写入

- **WHEN** Host 生成密钥配置或读取加密条目
- **THEN** nonce、tag 与密文按固定字节序组合，原子写入可完整回读，空串保持未配置

<!-- @anchor id=host-configuration-secrets -->
<!-- @nonCode -->

### Requirement: 配置验证依据

配置值与加密互操作验证 MUST 使用 `test/data/toolkitconfig/` 的配置、固定密钥和 `expected.json`。默认值依据 MUST 为该目录的 `defaults.json`，保留的 Host `DefaultToml` MUST 与其逐键一致。密钥测试 MUST 使用预置固定密钥；仅首次生成专项验证可使用随机密钥，断言限于长度、权限和往返。写入验证 MUST 包含自身读取回读，MUST NOT 修改原始夹具。保留参考测试不纳入现役构建或本次迁移执行。

#### Scenario: 校核保留配置合同

- **WHEN** 检视 Host 配置默认值或加密格式
- **THEN** 对照固定夹具与字节序验证，前端 WorkspaceConfiguration 不重新引入 TOML 互操作实现

<!-- @anchor id=host-configuration-validation -->
<!-- @nonCode -->

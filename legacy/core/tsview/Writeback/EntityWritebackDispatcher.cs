using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.TsView;

/// <summary>
/// 实体写回分发器。
/// 统一持久化入口，全部通过 ProjectModel.SaveChanges(parentEntity) 保存。
/// </summary>
/// <remarks>
/// <para>【持久化策略 - 全量重建】</para>
/// <para>1. 保留零件外壳（目录 + device.json）</para>
/// <para>2. 脚本组：清空后按 desiredScriptLists 声明式重建</para>
/// <para>3. 脚本：按 ScriptUpdates 声明式重建</para>
/// <para>4. CodeRepo 脚本：仅内容更新（实体由 refactor 操作管理）</para>
/// </remarks>
internal static class EntityWritebackDispatcher
{
    /// <summary>
    /// 统一持久化：全量重建零件内容，统一保存。
    /// </summary>
    /// <returns>实际受影响的相对路径列表（来自底层 Diff 计算）</returns>
    public static IReadOnlyList<string> Persist(
        ProjectModel model,
        DeviceTsView.CompleteBuildRequirements requirements,
        List<ToolError> warnings)
    {
        HashSet<string> affectedPaths = new(StringComparer.Ordinal);
        bool updatesCodeRepo = requirements.ScriptUpdates.Any(update => GuidText.Validate(update.Guid));
        WritableDeviceEntity device = requirements.Device;
        List<ProjectEntity> modifiedCodeRepos = [];
        ProjectEntity entityToSave = device;
        if (updatesCodeRepo)
        {
            WritableProjectRootEntity writableRoot = model.Root.ToWritable();
            entityToSave = writableRoot;

            ProjectEntity current = writableRoot;
            foreach (string segment in requirements.Device.实体_完整相对路径标识.Split('/'))
                current = ((IHasChildren<ProjectEntity>)current).Children.Single(child => child.实体_无相对路径标识符 == segment);
            device = (WritableDeviceEntity)current;
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // Step 1: 清空现有脚本组（外壳保留）
        // ═══════════════════════════════════════════════════════════════════════════════
        device.ScriptGroups.Clear();

        // ═══════════════════════════════════════════════════════════════════════════════
        // Step 2: 按 desiredScriptLists 声明式重建脚本组
        // ═══════════════════════════════════════════════════════════════════════════════
        Dictionary<string, WritableScriptGroupEntity> scriptGroupByDir = new(StringComparer.Ordinal);

        foreach ((string listPath, List<DeviceTsView.DesiredScriptListEntry> entries) in requirements.DesiredScriptLists)
        {
            if (entries.Count == 0)
            {
                // 空条目不创建脚本组
                continue;
            }

            string scriptGroupDir = RelativePathUtils.GetParentDirectory(listPath);
            string groupName = RelativePathUtils.GetLastSegment(scriptGroupDir);
            bool isEventGroup = groupName.StartsWith(ProjectEntityLayout.EventDirPrefix, StringComparison.Ordinal);

            WritableScriptGroupEntity group = new()
            {
                Parent = device,
                EventName = null
            };
            if (isEventGroup)
                group.EventName = groupName;
            group.SetScriptGroupNameInternal(groupName);

            // 设置 list.json 条目
            group.ScriptEntries = entries
                .Select(e => new ScriptEntryInfo(e.Name, e.Guid, e.IsCodeRepo, e.IsPrivate))
                .ToList();

            ListJsonItem[] items = entries
                .Select(e => new ListJsonItem(e.Name, e.Guid, e.IsCodeRepo, e.IsPrivate))
                .ToArray();
            group.ListJsonText = HjsonText.BuildListJsonText(items);

            device.ScriptGroups.Add(group);
            scriptGroupByDir[scriptGroupDir] = group;
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // Step 3: 按 ScriptUpdates 声明式重建脚本
        // ═══════════════════════════════════════════════════════════════════════════════
        foreach (DeviceTsView.ScriptUpdate update in requirements.ScriptUpdates)
        {
            string scriptRelativePath = update.SourcePath;
            bool isCodeRepo = GuidText.Validate(update.Guid);

            if (isCodeRepo)
            {
                // CodeRepo 与零件同属本次 TS 视图写回，必须随共同父实体一次保存。
                ProjectEntity current = entityToSave;
                foreach (string segment in scriptRelativePath.Split('/'))
                    current = ((IHasChildren<ProjectEntity>)current).Children.Single(child => child.实体_无相对路径标识符 == segment);
                ((WritableCodeRepoScriptEntity)current).SourceText = BuildScriptText(update);
                modifiedCodeRepos.Add(current);
            }
            else
            {
                // 内联脚本：找到对应脚本组，创建脚本
                string scriptDir = RelativePathUtils.GetParentDirectory(scriptRelativePath);

                if (!scriptGroupByDir.TryGetValue(scriptDir, out WritableScriptGroupEntity? group))
                {
                    throw new InvalidOperationException($"脚本组不存在: {scriptRelativePath}（list.json 中无声明）");
                }

                WritableInlineScriptEntity script = new()
                {
                    Parent = group,
                    SourceText = BuildScriptText(update)
                };
                script.SetScriptFileNameInternal(RelativePathUtils.GetLastSegment(scriptRelativePath));
                script.SetEventNameInternal(group.EventName);
                group.Scripts.Add(script);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // Step 4: 更新 vars.json（通过 Device）
        // ═══════════════════════════════════════════════════════════════════════════════
        if (requirements.DesiredGlobalVars.Count > 0 || device.HasVarsJson)
        {
            VarsJsonItem[] varsItems = requirements.DesiredGlobalVars
                .Select(v =>
                {
                    string typeName = v.Type.ToString();
                    string? comment = null;
                    if (!string.IsNullOrWhiteSpace(v.Comment))
                        comment = v.Comment;
                    return new VarsJsonItem(v.Name, typeName, comment);
                })
                .ToArray();

            device.Vars = new VarsConfig
            {
                Variables = varsItems
                    .Select(i => new VarInfo(i.Name, i.TypeName, i.Comment))
                    .ToList()
            };
            device.HasVarsJson = true;
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // Step 5: 统一保存
        // ═══════════════════════════════════════════════════════════════════════════════
        // 剪枝脏标记：本次修改的 device 分支与 CodeRepo 分支自底向上传播。
        // 上提保存（起点=根）时未标记的兄弟子树按剪枝声明跳过序列化与 diff 比较。
        model.MarkDirtyRecursive(device);
        foreach (ProjectEntity codeRepo in modifiedCodeRepos)
            model.MarkDirtyRecursive(codeRepo);

        IReadOnlyList<string> deviceAffected = model.SaveChanges(entityToSave);
        foreach (string p in deviceAffected) affectedPaths.Add(p);

        return affectedPaths.ToList();
    }

    internal static string BuildScriptText(DeviceTsView.ScriptUpdate update)
    {
        // 构建 BescriptHeader
        List<BescriptParameter> parameters = [];
        foreach (TsViewParamSpec param in update.Params)
        {
            if (TsViewTypeUtils.TryParseBescriptTypeNameFromTsTypeName(param.TypeName) is not string typeName)
            {
                throw new InvalidOperationException($"参数类型 '{param.TypeName}' 无法解析为原版类型，写回失败");
            }
            string rawName = TsViewIdentifierMap.FromTsIdentifier(param.Name);
            parameters.Add(new BescriptParameter(typeName, rawName));
        }

        BescriptParameter? returnParam = null;
        if (!string.Equals(update.ReturnType, "void", StringComparison.Ordinal))
        {
            if (TsViewTypeUtils.TryParseBescriptTypeNameFromTsTypeName(update.ReturnType) is not string typeName)
            {
                throw new InvalidOperationException($"返回类型 '{update.ReturnType}' 无法解析为原版类型，写回失败");
            }
            if (string.IsNullOrWhiteSpace(update.ReturnVarName))
            {
                throw new InvalidOperationException($"返回类型为 '{update.ReturnType}' 但缺少返回变量名，写回失败");
            }
            // ReturnVarName 来自 BeScript.retVar，已经是原始 BeScript 名，无需反转义
            returnParam = new BescriptParameter(typeName, update.ReturnVarName);
        }

        // 从 BeScript 声明获取 Color
        BescriptColor? color = null;
#pragma warning disable TK022 // Color 是记录可选字段，null 表示无颜色
        if (update.Color is { Count: 3 })
#pragma warning restore TK022
        {
            color = new BescriptColor(update.Color[0], update.Color[1], update.Color[2]);
        }

        BescriptHeader header = new(
            update.ScriptName,
            update.Comment,
            update.Debug,
            update.Skip,
            update.Private,
            update.Guid,
            color,
            parameters,
            returnParam);

        // 用正确的 Header 和 BodyAst 的 RootNodes 构建完整 AST
        BescriptAst ast = new(null, header, update.BodyAst.RootNodes, update.BodyAst.DeclaredVariables);

        return BescriptAstSerializer.SerializeFull(ast);
    }
}

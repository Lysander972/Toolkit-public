using Xunit;

namespace Toolkit.Core.Tests.Common;

/// <summary>
/// FileIO 测试集合定义 - 禁止与其他测试并行执行（修改全局 ProjectModel 状态）
/// </summary>
[CollectionDefinition("FileIO", DisableParallelization = true)]
public sealed class FileIOCollectionDefinition
{
}

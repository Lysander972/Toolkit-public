using Toolkit.Core.Infra;

namespace Toolkit.Core.Refactor;

public readonly struct RefactorResult<T> where T : class
{
    public T? Data { get; }
    public IReadOnlyList<ToolError> Errors { get; }
    public IReadOnlyList<ToolError> Warnings { get; }
    public bool IsSuccess => Errors.Count == 0 && Data != null;
    public T SuccessData
    {
        get
        {
            if (Data is T data)
                return data;
            throw new InvalidOperationException("RefactorResult 未成功时不能读取 SuccessData");
        }
    }

    private RefactorResult(T? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> warnings)
    { Data = data; Errors = errors; Warnings = warnings; }

    public static RefactorResult<T> Success(T data) => new(data, Array.Empty<ToolError>(), Array.Empty<ToolError>());
    public static RefactorResult<T> Success(T data, IReadOnlyList<ToolError> warnings) => new(data, Array.Empty<ToolError>(), warnings);
    public static RefactorResult<T> Fail(string message) => new(null, new[] { new ToolError(null, null, null, message) }, Array.Empty<ToolError>());
}

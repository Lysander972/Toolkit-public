namespace Toolkit.Core.TsView;

internal abstract record TsViewDiagnosticTag
{
    public sealed record NoneTag : TsViewDiagnosticTag;
    public sealed record SubjectTag(string Code, string Name) : TsViewDiagnosticTag;

    public static readonly TsViewDiagnosticTag None = new NoneTag();

    public static TsViewDiagnosticTag ForSubject(string code, string name) => new SubjectTag(code, name);
}

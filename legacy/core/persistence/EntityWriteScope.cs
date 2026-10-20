namespace Toolkit.Core.Persistence;

internal static class EntityWriteScope
{
    // 业务写入统一规则：
    // - SaveChanges 每次都会执行持久化并通过统一 timer 排期刷新，不适合在同一轮业务中拆开多次调用。
    // - 调用方应先合并修改，原则上每轮业务只提交一个可写实体；需要同时修改多处时应上提到共同父实体。
    // - 本封装只负责把业务修改收束到一次 SaveChanges，禁止在这里制造分散刷新路径。

    public static IReadOnlyList<string> ApplyContentChange<TReadOnly, TWritable>(
        ProjectModel model,
        TReadOnly readOnly,
        Func<TReadOnly, TWritable> toWritable,
        Action<TWritable> mutate)
        where TReadOnly : ProjectEntity
        where TWritable : ProjectEntity, IWritableEntity
    {
        TWritable writable = toWritable(readOnly);
        mutate(writable);
        return model.SaveChanges(writable);
    }

    public static IReadOnlyList<string> ApplyCreate<TReadOnlyParent, TWritableParent, TNewEntity>(
        ProjectModel model,
        TReadOnlyParent readOnlyParent,
        Func<TReadOnlyParent, TWritableParent> toWritableParent,
        Func<TWritableParent, TNewEntity> create,
        Action<TWritableParent, TNewEntity> attach)
        where TReadOnlyParent : ProjectEntity
        where TWritableParent : ProjectEntity, IWritableEntity
        where TNewEntity : ProjectEntity
    {
        TWritableParent writableParent = toWritableParent(readOnlyParent);
        TNewEntity newEntity = create(writableParent);

        attach(writableParent, newEntity);
        return model.SaveChanges(writableParent);
    }

    public static IReadOnlyList<string> ApplyDelete<TReadOnlyParent, TWritableParent>(
        ProjectModel model,
        TReadOnlyParent readOnlyParent,
        Func<TReadOnlyParent, TWritableParent> toWritableParent,
        Action<TWritableParent> remove)
        where TReadOnlyParent : ProjectEntity
        where TWritableParent : ProjectEntity, IWritableEntity
    {
        TWritableParent writableParent = toWritableParent(readOnlyParent);
        remove(writableParent);
        return model.SaveChanges(writableParent);
    }
}

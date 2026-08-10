namespace Raindrop.Utility.Extensions;

public static class ListExtensions
{
    public static void BatchExecute<T>(this IEnumerable<T> source, int page, int batchSize, Action<IEnumerable<T>> action)
    {
        var count = source.Count();
        int totalPage;
        if (count % batchSize == 0)
            totalPage = count / batchSize;
        else
            totalPage = count / batchSize + 1;

        if (page <= totalPage)
        {
            var list = source.Skip((page - 1) * batchSize).Take(batchSize).ToList();
            action(list);
            if (page < totalPage)
                BatchExecute(source, page + 1, batchSize, action);
        }
    }

    public static IEnumerable<T> WhereIf<T>(this IEnumerable<T> source, bool condition, Func<T, bool> predicate)
    {
        if (condition)
        {
            return source.Where(predicate);
        }
        return source;
    }

    public static IEnumerable<IEnumerable<T>> GetBatches<T>(this IEnumerable<T> source, int batchSize)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be greater than 0.");
        }

        using var enumerator = source.GetEnumerator();
        while (enumerator.MoveNext())
        {
            yield return GetNextBatch(enumerator, batchSize);
        }
    }

    /// <summary>
    /// 对有序的集合进行插入排序
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="list"></param>
    /// <param name="newObj"></param>
    /// <param name="func"></param>
    public static void InsertShort<T>(this List<T> list, T newObj, Func<T, IComparable> func)
    {
        var index = list.FindIndex(x => func(x).CompareTo(func(newObj)) > 0);
        if (index < 0)
        {
            index = list.Count;
        }
        list.Insert(index, newObj);
    }

    private static IEnumerable<T> GetNextBatch<T>(IEnumerator<T> enumerator, int batchSize)
    {
        var batch = new List<T>(batchSize);
        do
        {
            batch.Add(enumerator.Current);
        } while (batch.Count < batchSize && enumerator.MoveNext());

        return batch;
    }
}

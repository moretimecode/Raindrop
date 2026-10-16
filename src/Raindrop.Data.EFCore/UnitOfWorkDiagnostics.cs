namespace Raindrop.Data.EFCore;

/// <summary>诊断是尽力副作用，日志提供者故障不能覆盖业务/提交结果或中断清理。</summary>
internal static class UnitOfWorkDiagnostics
{
    public static void TryLog(Action writeLog)
    {
        try
        {
            writeLog();
        }
        catch (Exception)
        {
            // 只隔离日志提供者异常，不再次调用故障日志链，避免掩盖原结果或递归报错。
        }
    }
}

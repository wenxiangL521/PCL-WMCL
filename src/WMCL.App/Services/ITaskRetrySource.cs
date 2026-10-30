namespace WMCL.App.Services;

/// <summary>
/// 任务失败上报与重试行为契约：源 ViewModel 在失败终态后可被任务中心读取失败堆栈并按原参数重新触发。
/// RetryAsync 返回 true=已受理重新执行；false=目标失效（如版本已被删除、选择已变化）。
/// </summary>
public interface ITaskRetrySource
{
    /// <summary>最近一次失败任务的堆栈载荷；无失败时为 null。消费后由任务中心清空。</summary>
    TaskFailureInfo? LastTaskFailure { get; }

    /// <summary>返回最近一次失败任务的重试快照；无可重试任务时返回 null。</summary>
    TaskRetryPayload? CaptureRetryPayload();

    /// <summary>按快照重新执行既有安装方法。内部异常不外抛（沿既有 catch→Status 模式）。</summary>
    Task<bool> RetryAsync(TaskRetryPayload payload);

    /// <summary>一次性消费：清空失败堆栈与重试快照。</summary>
    void ClearTaskFailure();
}

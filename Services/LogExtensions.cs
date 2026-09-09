using System;

namespace MoviePilot_V3.Services
{
    /// <summary>
    /// 日志级别扩展（配合贯穿全项目的 Action&lt;string&gt; 日志回调使用）：
    /// 普通进度 / 状态日志直接 log("...")（默认 INFO 级别，原样输出）；
    /// 警告与错误显式调用 log.Warn / log.Error，由本扩展统一添加英文级别前缀
    /// [WARN] / [ERROR]，使 sink（面板运行日志区 / cmd.log / shutdown.log）能直观区分严重程度。
    /// 级别语义：Info = 正常进度与结果；Warn = 可恢复 / 会重试 / 跳过继续；
    /// Error = 终结性失败（流程中止或放弃）。
    /// </summary>
    public static class LogExtensions
    {
        /// <summary>普通日志（等价直接 log(message)，保持显式级别写法便于阅读）。</summary>
        public static void Info(this Action<string> log, string message)
        {
            log(message);
        }

        /// <summary>警告日志：可恢复异常、自动重试、跳过但流程继续等。</summary>
        public static void Warn(this Action<string> log, string message)
        {
            log("[WARN] " + message);
        }

        /// <summary>错误日志：终结性失败（流程中止 / 放弃重试 / 校验不过）。</summary>
        public static void Error(this Action<string> log, string message)
        {
            log("[ERROR] " + message);
        }
    }
}

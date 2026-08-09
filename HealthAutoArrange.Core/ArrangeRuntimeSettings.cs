using System;

namespace HealthAutoArrange.Core
{
    /// <summary>
    /// 运行时设置：控制排序/提醒是否生效。纯 C#，无 Unity 依赖。
    /// 由 UiConfigModel.Enabled 经 <see cref="FromModel"/> 传入，
    /// 运行时（Adapter）在禁用时不排序、不触发提醒，但保留诊断与配置 UI。
    /// </summary>
    public sealed class ArrangeRuntimeSettings
    {
        /// <summary>是否启用排序与提醒（默认启用）。</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>运行时决策：禁用时不应执行排序/提醒。</summary>
        public bool ShouldRun() => Enabled;

        /// <summary>从 UI 模型派生运行时设置。</summary>
        public static ArrangeRuntimeSettings FromModel(UiConfigModel model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            return new ArrangeRuntimeSettings { Enabled = model.Enabled };
        }
    }
}
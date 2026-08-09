using System;

namespace HealthAutoArrange.Core
{
    /// <summary>
    /// 运行时设置：控制状态图标排序是否生效。纯 C#，无 Unity 依赖。
    /// 由 UiConfigModel.Enabled 经 <see cref="FromModel"/> 传入。提醒规则拥有自己的 Enabled，
    /// 因此关闭排序不会静默关闭提醒；状态观察与诊断也继续工作。
    /// </summary>
    public sealed class ArrangeRuntimeSettings
    {
        /// <summary>是否启用状态图标排序（默认启用）。</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>运行时决策：禁用时不应写入 UI 排序。</summary>
        public bool ShouldRun() => Enabled;

        /// <summary>从 UI 模型派生运行时设置。</summary>
        public static ArrangeRuntimeSettings FromModel(UiConfigModel model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            return new ArrangeRuntimeSettings { Enabled = model.Enabled };
        }
    }
}
namespace HealthAutoArrange.Core
{
    /// <summary>
    /// 未知状态（未在任何分组中声明的状态）的排序策略。
    /// </summary>
    public enum UnknownStatePolicy
    {
        /// <summary>未知状态置于末尾，保持相对顺序。</summary>
        End = 0,

        /// <summary>未知状态保持在原始位置，已知状态按优先级填入其余位置（默认/推荐）。</summary>
        Keep = 1,
    }
}
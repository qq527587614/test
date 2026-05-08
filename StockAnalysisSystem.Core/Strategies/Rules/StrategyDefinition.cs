using System.Text.Json.Serialization;

namespace StockAnalysisSystem.Core.Strategies.Rules;

/// <summary>
/// 可组合策略定义：由入场/出场规则树组成，便于 UI 构建与回测复用。
/// </summary>
public sealed class StrategyDefinition
{
    public string Name { get; set; } = "UnnamedStrategy";

    /// <summary>买入条件（为 null 表示永不买入）。</summary>
    public IRuleNode? EntryRule { get; set; }

    /// <summary>卖出条件（为 null 表示不主动卖出）。</summary>
    public IRuleNode? ExitRule { get; set; }

    /// <summary>策略附加参数（可用于 UI 侧保存阈值/备注）。</summary>
    public Dictionary<string, object> Parameters { get; set; } = new();
}

/// <summary>
/// 规则节点接口（为 JSON 序列化预留 Type 字段）。
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(AndNode), "and")]
[JsonDerivedType(typeof(OrNode), "or")]
[JsonDerivedType(typeof(NotNode), "not")]
[JsonDerivedType(typeof(CompareNode), "compare")]
[JsonDerivedType(typeof(CrossOverNode), "crossOver")]
[JsonDerivedType(typeof(CrossUnderNode), "crossUnder")]
[JsonDerivedType(typeof(FirstBoardPullbackNode), "firstBoardPullback")]
public interface IRuleNode
{
    bool Evaluate(RuleContext ctx);
}


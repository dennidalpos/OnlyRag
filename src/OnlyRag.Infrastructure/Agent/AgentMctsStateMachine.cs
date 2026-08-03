using System.Collections.Concurrent;

namespace OnlyRag.Infrastructure.Agent;

public sealed class AgentMctsNode
{
    public string NodeId { get; } = Guid.NewGuid().ToString("N")[..8];
    public AgentMctsNode? Parent { get; }
    public List<AgentMctsNode> Children { get; } = new();
    public string ActionSignature { get; }
    public string? OutputSnippet { get; set; }
    public double VisitCount { get; set; }
    public double TotalReward { get; set; }
    public double ReflectionScore { get; set; }
    public string? CheckpointId { get; set; }
    public bool IsTerminal { get; set; }

    public AgentMctsNode(string actionSignature, AgentMctsNode? parent = null)
    {
        ActionSignature = actionSignature;
        Parent = parent;
    }

    public double MeanReward => VisitCount > 0 ? TotalReward / VisitCount : 0;

    public double CalculateUct(double explorationConstant = 1.414)
    {
        if (VisitCount == 0) return double.MaxValue;
        double parentVisits = Parent?.VisitCount ?? 1;
        return MeanReward + explorationConstant * Math.Sqrt(Math.Log(parentVisits) / VisitCount);
    }
}

/// <summary>
/// Tree-of-Thought (ToT) Monte Carlo Tree Search (MCTS) state machine for agent action selection,
/// branching, simulation scoring, and backpropagation.
/// </summary>
public sealed class AgentMctsStateMachine
{
    private readonly AgentMctsNode rootNode;
    private AgentMctsNode currentActiveNode;
    private readonly WorkspaceSnapshotCheckpointManager checkpointManager;

    public AgentMctsNode Root => rootNode;
    public AgentMctsNode CurrentActiveNode => currentActiveNode;

    public AgentMctsStateMachine(WorkspaceSnapshotCheckpointManager checkpointManager, string initialGoal)
    {
        this.checkpointManager = checkpointManager;
        this.rootNode = new AgentMctsNode($"Goal:{initialGoal}");
        this.currentActiveNode = this.rootNode;
    }

    /// <summary>
    /// Selects the best candidate leaf node using Upper Confidence Bound for Trees (UCT).
    /// </summary>
    public AgentMctsNode SelectBestNode(AgentMctsNode current)
    {
        if (current.Children.Count == 0) return current;

        AgentMctsNode bestChild = current.Children[0];
        double bestUct = double.MinValue;

        foreach (var child in current.Children)
        {
            double uct = child.CalculateUct();
            if (uct > bestUct)
            {
                bestUct = uct;
                bestChild = child;
            }
        }

        return current.Children.Count > 0 && !bestChild.IsTerminal ? SelectBestNode(bestChild) : bestChild;
    }

    /// <summary>
    /// Expands candidate action nodes under the specified parent node.
    /// </summary>
    public AgentMctsNode Expand(AgentMctsNode parent, string actionSignature)
    {
        var child = new AgentMctsNode(actionSignature, parent);
        parent.Children.Add(child);
        return child;
    }

    /// <summary>
    /// Expands candidate action nodes under the active node and navigates to it.
    /// </summary>
    public AgentMctsNode ExpandAndNavigate(string actionSignature, string? checkpointId = null)
    {
        var child = new AgentMctsNode(actionSignature, currentActiveNode)
        {
            CheckpointId = checkpointId
        };
        currentActiveNode.Children.Add(child);
        currentActiveNode = child;
        return child;
    }


    /// <summary>
    /// Evaluates execution outcome and assigns reward score between 0.0 (failure/error) and 1.0 (success).
    /// </summary>
    public static double EvaluateReward(bool success, bool hasCompilationError, double reflectionScore)
    {
        if (!success) return 0.0;
        if (hasCompilationError) return 0.2;
        double baseReward = 0.7;
        return Math.Clamp(baseReward + (reflectionScore * 0.3), 0.0, 1.0);
    }

    /// <summary>
    /// Evaluates outcome for the current active node, backpropagates reward, and flags terminal state or prunes branches on low reflection score.
    /// </summary>
    public double EvaluateAndBackpropagateCurrent(bool success, bool hasCompilationError, double reflectionScore = 0.5, double minReflectionThreshold = 0.25)
    {
        double reward = EvaluateReward(success, hasCompilationError, reflectionScore);
        currentActiveNode.ReflectionScore = reflectionScore;

        if (!success || reflectionScore < minReflectionThreshold)
        {
            currentActiveNode.IsTerminal = true;
        }

        Backpropagate(currentActiveNode, reward);
        PruneLowReflectionBranches(minReflectionThreshold);
        return reward;
    }

    /// <summary>
    /// Prunes branches in the MCTS tree whose reflection score falls below the minimum threshold.
    /// </summary>
    public int PruneLowReflectionBranches(double minReflectionThreshold = 0.25)
    {
        return PruneNodeRecursive(rootNode, minReflectionThreshold);
    }

    private int PruneNodeRecursive(AgentMctsNode node, double minReflectionThreshold)
    {
        int prunedCount = 0;
        for (int i = node.Children.Count - 1; i >= 0; i--)
        {
            var child = node.Children[i];
            if (child.VisitCount > 0 && child.ReflectionScore < minReflectionThreshold)
            {
                child.IsTerminal = true;
                node.Children.RemoveAt(i);
                prunedCount++;
            }
            else
            {
                prunedCount += PruneNodeRecursive(child, minReflectionThreshold);
            }
        }
        return prunedCount;
    }

    /// <summary>
    /// Backpropagates reward score up the tree from leaf node to root.
    /// </summary>
    public void Backpropagate(AgentMctsNode node, double reward)
    {
        AgentMctsNode? current = node;
        while (current != null)
        {
            current.VisitCount++;
            current.TotalReward += reward;
            current = current.Parent;
        }
    }

    /// <summary>
    /// Reverts active position to parent node in case of branch rollback.
    /// </summary>
    public AgentMctsNode NavigateToParent()
    {
        if (currentActiveNode.Parent != null)
        {
            currentActiveNode = currentActiveNode.Parent;
        }
        return currentActiveNode;
    }
}


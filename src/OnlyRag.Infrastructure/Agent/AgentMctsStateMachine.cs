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

    public AgentMctsNode(string actionSignature, AgentMctsNode? parent = null, string? nodeId = null)
    {
        ActionSignature = actionSignature;
        Parent = parent;
        if (!string.IsNullOrWhiteSpace(nodeId))
        {
            NodeId = nodeId;
        }
    }

    public double MeanReward => VisitCount > 0 ? TotalReward / VisitCount : 0;

    public double CalculateUct(double explorationConstant = 1.414)
    {
        if (VisitCount == 0) return double.MaxValue;
        double parentVisits = Parent?.VisitCount ?? 1;
        return MeanReward + explorationConstant * Math.Sqrt(Math.Log(parentVisits) / VisitCount);
    }
}

public sealed record AgentMctsNodeSnapshotDto(
    string NodeId,
    string? ParentId,
    string ActionSignature,
    string? OutputSnippet,
    double VisitCount,
    double TotalReward,
    double ReflectionScore,
    string? CheckpointId,
    bool IsTerminal,
    List<string> ChildIds);

public sealed record AgentMctsTreeSnapshotDto(
    string ActiveNodeId,
    List<AgentMctsNodeSnapshotDto> Nodes);

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

    private AgentMctsStateMachine(WorkspaceSnapshotCheckpointManager checkpointManager, AgentMctsNode rootNode)
    {
        this.checkpointManager = checkpointManager;
        this.rootNode = rootNode;
        this.currentActiveNode = rootNode;
    }

    public string ToSnapshotJson()
    {
        List<AgentMctsNodeSnapshotDto> nodeDtos = new();
        CollectNodesRecursive(rootNode, nodeDtos);
        var snapshot = new AgentMctsTreeSnapshotDto(currentActiveNode.NodeId, nodeDtos);
        return System.Text.Json.JsonSerializer.Serialize(snapshot);
    }

    private static void CollectNodesRecursive(AgentMctsNode node, List<AgentMctsNodeSnapshotDto> list)
    {
        list.Add(new AgentMctsNodeSnapshotDto(
            node.NodeId,
            node.Parent?.NodeId,
            node.ActionSignature,
            node.OutputSnippet,
            node.VisitCount,
            node.TotalReward,
            node.ReflectionScore,
            node.CheckpointId,
            node.IsTerminal,
            node.Children.Select(c => c.NodeId).ToList()));

        foreach (var child in node.Children)
        {
            CollectNodesRecursive(child, list);
        }
    }

    public static AgentMctsStateMachine FromSnapshotJson(WorkspaceSnapshotCheckpointManager checkpointManager, string json)
    {
        var snapshot = System.Text.Json.JsonSerializer.Deserialize<AgentMctsTreeSnapshotDto>(json);
        if (snapshot == null || snapshot.Nodes.Count == 0)
        {
            throw new InvalidOperationException("Invalid MCTS snapshot JSON.");
        }

        var nodeDtoMap = snapshot.Nodes.ToDictionary(n => n.NodeId);
        var rootDto = snapshot.Nodes.FirstOrDefault(n => n.ParentId == null) ?? snapshot.Nodes[0];

        var instances = new Dictionary<string, AgentMctsNode>();

        AgentMctsNode BuildNode(AgentMctsNodeSnapshotDto dto, AgentMctsNode? parent)
        {
            var node = new AgentMctsNode(dto.ActionSignature, parent, dto.NodeId)
            {
                OutputSnippet = dto.OutputSnippet,
                VisitCount = dto.VisitCount,
                TotalReward = dto.TotalReward,
                ReflectionScore = dto.ReflectionScore,
                CheckpointId = dto.CheckpointId,
                IsTerminal = dto.IsTerminal
            };
            instances[dto.NodeId] = node;

            foreach (var childId in dto.ChildIds)
            {
                if (nodeDtoMap.TryGetValue(childId, out var childDto))
                {
                    var childNode = BuildNode(childDto, node);
                    node.Children.Add(childNode);
                }
            }

            return node;
        }

        var rootNode = BuildNode(rootDto, null);
        var machine = new AgentMctsStateMachine(checkpointManager, rootNode);
        if (instances.TryGetValue(snapshot.ActiveNodeId, out var active))
        {
            machine.currentActiveNode = active;
        }

        return machine;
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

    /// <summary>
    /// Computes a pre-execution candidate action heuristic score based on safety and operation type.
    /// </summary>
    public static double EvaluateCandidateActionHeuristic(string toolName, string argumentsJson)
    {
        double score = 0.5; // Baseline heuristic score

        // Read-only tools have low risk and high exploration value
        if (toolName.Equals("read_file", StringComparison.OrdinalIgnoreCase) ||
            toolName.Equals("list_dir", StringComparison.OrdinalIgnoreCase) ||
            toolName.Equals("grep_search", StringComparison.OrdinalIgnoreCase) ||
            toolName.Equals("git_diff_inspect", StringComparison.OrdinalIgnoreCase) ||
            toolName.Equals("query_retrieval_index", StringComparison.OrdinalIgnoreCase))
        {
            score += 0.3;
        }
        // Planning and reflection tools align with SOTA agent execution phases
        else if (toolName.Equals("plan_task", StringComparison.OrdinalIgnoreCase) ||
                 toolName.Equals("reflect_step", StringComparison.OrdinalIgnoreCase))
        {
            score += 0.4;
        }
        // AST structural refactoring and diff patch tools are targeted modifications
        else if (toolName.Equals("ast_structural_refactor", StringComparison.OrdinalIgnoreCase) ||
                 toolName.Equals("apply_diff_patch", StringComparison.OrdinalIgnoreCase))
        {
            score += 0.2;
        }
        // Code mutation tools are higher risk, moderate default score
        else if (toolName.Equals("write_file", StringComparison.OrdinalIgnoreCase) ||
                 toolName.Equals("replace_file_content", StringComparison.OrdinalIgnoreCase) ||
                 toolName.Equals("multi_replace_file_content", StringComparison.OrdinalIgnoreCase))
        {
            score += 0.1;
        }
        // Direct command execution requires validation
        else if (toolName.Equals("run_command", StringComparison.OrdinalIgnoreCase))
        {
            score += 0.15;
        }

        return Math.Clamp(score, 0.0, 1.0);
    }

    /// <summary>
    /// Selects the optimal non-terminal candidate child under current active node using Upper Confidence Bound for Trees (UCT).
    /// </summary>
    public AgentMctsNode? SelectBestCandidateChild()
    {
        var validChildren = currentActiveNode.Children.Where(c => !c.IsTerminal).ToList();
        if (validChildren.Count == 0) return null;

        AgentMctsNode bestChild = validChildren[0];
        double bestUct = double.MinValue;

        foreach (var child in validChildren)
        {
            double uct = child.CalculateUct();
            if (uct > bestUct)
            {
                bestUct = uct;
                bestChild = child;
            }
        }

        return bestChild;
    }

    /// <summary>
    /// Prunes a failed candidate branch, flags it terminal, and reverts active position to parent node.
    /// </summary>
    public AgentMctsNode PruneAndRollbackActiveBranch()
    {
        currentActiveNode.IsTerminal = true;
        currentActiveNode.ReflectionScore = 0.0;
        return NavigateToParent();
    }
}


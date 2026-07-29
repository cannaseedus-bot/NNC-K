#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// Supernaut Planning Skill — decomposes high-level objectives into
    /// deterministic task graphs with dependencies, priorities, validation
    /// points, and follow-up skill recommendations.
    ///
    /// Orchestrated by Supernaut; never schedules rounds directly.
    /// Returns structured recommendations to the orchestrator.
    /// </summary>
    public sealed class TaskPlanner
    {
        private readonly Dictionary<string, SkillRegistry> _skillCatalog;
        private const int MaxAttempts = 3;

        public TaskPlanner()
        {
            _skillCatalog = new Dictionary<string, SkillRegistry>(StringComparer.OrdinalIgnoreCase)
            {
                ["memory"] = new SkillRegistry("memory", "read/write context"),
                ["validator"] = new SkillRegistry("validator", "verify task outputs"),
                ["advisor"] = new SkillRegistry("advisor", "review and suggest"),
                ["translator"] = new SkillRegistry("translator", "transform formats"),
                ["search"] = new SkillRegistry("search", "external retrieval"),
                ["coder"] = new SkillRegistry("coder", "implement tasks"),
                ["reasoning"] = new SkillRegistry("reasoning", "analyze and infer"),
                ["task_generation"] = new SkillRegistry("task_generation", "produce sub-tasks"),
                ["task_decomposition"] = new SkillRegistry("task_decomposition", "break down work")
            };
        }

        // ── Plan ─────────────────────────────────────────────────────────

        /// <summary>Convert an objective into a structured task graph.</summary>
        public TaskPlanResult Plan(
            string objective,
            List<string>? constraints = null,
            Dictionary<string, object>? context = null,
            Dictionary<string, object>? memory = null,
            TaskPlanResult? previousRound = null)
        {
            if (string.IsNullOrWhiteSpace(objective))
                return Failed("Objective is required.");

            var plan = new TaskPlan
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 12),
                Objective = objective,
                Constraints = constraints ?? new List<string>(),
                Context = context ?? new Dictionary<string, object>(),
                Created = DateTime.UtcNow
            };

            // 1. Analyze the objective and decompose into tasks
            var tasks = Decompose(objective, constraints, previousRound);

            // 2. Identify dependencies and risks
            var deps = IdentifyDependencies(tasks, previousRound);

            // 3. Assign priorities
            var prioritized = AssignPriorities(tasks);

            // 4. Build the task graph
            plan.Tasks = prioritized;
            plan.Dependencies = deps;

            // 5. Validate the graph
            var validation = ValidatePlan(plan);

            // 6. Recommend next steps
            var recommendations = RecommendFollowUps(plan, validation);

            var confidence = EstimateConfidence(tasks.Count, deps.Count, validation.IsValid);

            return new TaskPlanResult
            {
                Status = confidence >= 0.5 ? PlanStatus.Success : PlanStatus.Partial,
                Confidence = confidence,
                Plan = plan,
                Validation = validation,
                NextRecommendations = recommendations,
                Metadata = new PlanMetadata
                {
                    TaskCount = tasks.Count,
                    DependencyCount = deps.Count,
                    Confidence = confidence,
                    PreviousRoundId = previousRound?.Plan?.Id
                }
            };
        }

        // ── Decomposition ────────────────────────────────────────────────

        private List<TaskItem> Decompose(
            string objective,
            List<string>? constraints,
            TaskPlanResult? previousRound)
        {
            var tasks = new List<TaskItem>();

            // Phase 1: Context and analysis
            tasks.Add(new TaskItem
            {
                Id = "T1",
                Title = "Analyze objective and load constraints",
                Phase = "Pop",
                Priority = Priority.High,
                Dependencies = Array.Empty<string>(),
                EstimatedEffort = "medium"
            });

            // Phase 2: Design and plan
            tasks.Add(new TaskItem
            {
                Id = "T2",
                Title = "Design approach and identify sub-components",
                Phase = "Wo",
                Priority = Priority.High,
                Dependencies = new[] { "T1" },
                EstimatedEffort = "medium"
            });

            // Phase 3: Build
            tasks.Add(new TaskItem
            {
                Id = "T3",
                Title = $"Implement primary work for: {Truncate(objective, 80)}",
                Phase = "Sek",
                Priority = Priority.Critical,
                Dependencies = new[] { "T2" },
                EstimatedEffort = "large"
            });

            // Phase 4: Validate
            tasks.Add(new TaskItem
            {
                Id = "T4",
                Title = "Validate output against constraints",
                Phase = "Yax",
                Priority = Priority.High,
                Dependencies = new[] { "T3" },
                EstimatedEffort = "small"
            });

            // Phase 5: Review and refine
            tasks.Add(new TaskItem
            {
                Id = "T5",
                Title = "Review, refine, and document results",
                Phase = "Ch'en",
                Priority = Priority.Medium,
                Dependencies = new[] { "T4" },
                EstimatedEffort = "small"
            });

            // Phase 6: Consolidate
            tasks.Add(new TaskItem
            {
                Id = "T6",
                Title = "Consolidate artifacts and record replay",
                Phase = "Xul",
                Priority = Priority.Low,
                Dependencies = new[] { "T5" },
                EstimatedEffort = "small"
            });

            // Carry forward incomplete tasks from previous round
            if (previousRound?.Plan?.Tasks != null)
            {
                foreach (var prev in previousRound.Plan.Tasks.Where(t => !t.Completed))
                {
                    var retry = new TaskItem
                    {
                        Id = prev.Id + "_retry",
                        Title = prev.Title,
                        Phase = prev.Phase,
                        Priority = prev.Priority,
                        Dependencies = prev.Dependencies,
                        EstimatedEffort = prev.EstimatedEffort,
                        RetryCount = prev.RetryCount + 1
                    };

                    if (retry.RetryCount < MaxAttempts)
                        tasks.Add(retry);
                }
            }

            return tasks;
        }

        // ── Dependencies ─────────────────────────────────────────────────

        private List<TaskDependency> IdentifyDependencies(
            List<TaskItem> tasks,
            TaskPlanResult? previousRound)
        {
            var deps = new List<TaskDependency>();

            foreach (var task in tasks)
            {
                foreach (var depId in task.Dependencies)
                {
                    var dep = new TaskDependency
                    {
                        FromId = depId,
                        ToId = task.Id,
                        Kind = DependencyKind.Blocking
                    };

                    // Check if the dependency was partially satisfied in a previous round
                    if (previousRound?.Plan?.Tasks != null)
                    {
                        var prevTask = previousRound.Plan.Tasks
                            .FirstOrDefault(t => t.Id == depId);
                        if (prevTask?.Completed == true)
                            dep.Kind = DependencyKind.Satisfied;
                    }

                    deps.Add(dep);
                }
            }

            return deps;
        }

        // ── Priority ─────────────────────────────────────────────────────

        private static List<TaskItem> AssignPriorities(List<TaskItem> tasks)
        {
            // Priority is already set during decomposition.
            // Sort by priority descending.
            return tasks
                .OrderByDescending(t => t.Priority)
                .ThenBy(t => t.PhaseOrder)
                .ToList();
        }

        // ── Validation ───────────────────────────────────────────────────

        public PlanValidation ValidatePlan(TaskPlan plan)
        {
            var issues = new List<string>();

            // Check for missing task references in dependencies
            var taskIds = new HashSet<string>(plan.Tasks.Select(t => t.Id));
            foreach (var task in plan.Tasks)
            {
                foreach (var depId in task.Dependencies)
                {
                    if (!taskIds.Contains(depId))
                        issues.Add($"Task '{task.Id}' depends on missing task '{depId}'");
                }
            }

            // Check for circular dependencies
            var visited = new HashSet<string>();
            var inStack = new HashSet<string>();
            foreach (var task in plan.Tasks)
            {
                if (HasCycle(task.Id, plan.Tasks.ToDictionary(t => t.Id), visited, inStack))
                {
                    issues.Add($"Circular dependency detected involving task '{task.Id}'");
                    break;
                }
            }

            // Check for orphaned tasks (no dependencies, not a root)
            var hasRoot = plan.Tasks.Any(t => t.Dependencies.Length == 0);
            if (!hasRoot)
                issues.Add("No root task found (all tasks have dependencies)");

            return new PlanValidation
            {
                IsValid = issues.Count == 0,
                Issues = issues,
                TaskCount = plan.Tasks.Count,
                DependencyCount = plan.Dependencies.Count,
                EstimatedRounds = Math.Max(1, plan.Dependencies.Count(d => d.Kind == DependencyKind.Blocking))
            };
        }

        private static bool HasCycle(
            string nodeId,
            Dictionary<string, TaskItem> graph,
            HashSet<string> visited,
            HashSet<string> inStack)
        {
            if (inStack.Contains(nodeId)) return true;
            if (visited.Contains(nodeId)) return false;

            visited.Add(nodeId);
            inStack.Add(nodeId);

            if (graph.TryGetValue(nodeId, out var node))
            {
                foreach (var depId in node.Dependencies)
                {
                    if (HasCycle(depId, graph, visited, inStack))
                        return true;
                }
            }

            inStack.Remove(nodeId);
            return false;
        }

        // ── Confidence ───────────────────────────────────────────────────

        private static double EstimateConfidence(
            int taskCount, int depCount, bool valid)
        {
            if (taskCount == 0) return 0;
            if (!valid) return 0.3;

            var baseScore = Math.Min(1.0, taskCount / 10.0);
            var depScore = depCount > 0 ? Math.Min(1.0, depCount / (double)taskCount) : 0.5;

            return Math.Round((baseScore * 0.6 + depScore * 0.4), 2);
        }

        // ── Recommendations ──────────────────────────────────────────────

        private List<SkillRecommendation> RecommendFollowUps(
            TaskPlan plan, PlanValidation validation)
        {
            var recs = new List<SkillRecommendation>();

            if (!validation.IsValid)
            {
                recs.Add(new SkillRecommendation("validator", "Review plan structure and fix issues"));
            }

            // Recommend based on task complexity
            var complexTasks = plan.Tasks.Count(t => t.EstimatedEffort == "large");
            if (complexTasks > 0)
            {
                recs.Add(new SkillRecommendation("task_decomposition",
                    $"Break down {complexTasks} large task(s) into smaller sub-tasks"));
            }

            // Recommend coder for implementation-heavy plans
            var sekTasks = plan.Tasks.Count(t => t.Phase == "Sek");
            if (sekTasks > 0)
            {
                recs.Add(new SkillRecommendation("coder",
                    $"Implement {sekTasks} task(s) in Sek phase"));
            }

            // Recommend reasoning for complex objectives
            if (plan.Objective.Length > 100)
            {
                recs.Add(new SkillRecommendation("reasoning",
                    "Deep analysis recommended for complex objective"));
            }

            // Always recommend memory for persistence
            recs.Add(new SkillRecommendation("memory", "Persist context and artifacts"));

            return recs;
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static TaskPlanResult Failed(string reason) => new()
        {
            Status = PlanStatus.Failed,
            Confidence = 0,
            Validation = new PlanValidation
            {
                IsValid = false,
                Issues = new List<string> { reason }
            },
            NextRecommendations = new List<SkillRecommendation>
            {
                new("reasoning", $"Review failure: {reason}")
            },
            Metadata = new PlanMetadata { Confidence = 0 }
        };

        private static string Truncate(string value, int maxLength) =>
            value.Length <= maxLength ? value : value[..maxLength] + "...";
    }

    // ── Data Types ─────────────────────────────────────────────────────

    public sealed class TaskPlan
    {
        public string Id { get; init; } = "";
        public string Objective { get; init; } = "";
        public List<string> Constraints { get; init; } = new();
        public Dictionary<string, object> Context { get; init; } = new();
        public List<TaskItem> Tasks { get; set; } = new();
        public List<TaskDependency> Dependencies { get; set; } = new();
        public DateTime Created { get; init; } = DateTime.UtcNow;
    }

    public sealed class TaskItem
    {
        public string Id { get; init; } = "";
        public string Title { get; init; } = "";
        public string Phase { get; init; } = "Pop";
        public Priority Priority { get; init; } = Priority.Medium;
        public string[] Dependencies { get; init; } = Array.Empty<string>();
        public string EstimatedEffort { get; init; } = "medium";
        public bool Completed { get; set; }
        public int RetryCount { get; init; }

        public int PhaseOrder => Phase switch
        {
            "Pop" => 0,
            "Wo" => 1,
            "Yax" => 2,
            "Sek" => 3,
            "Ch'en" => 4,
            "Xul" => 5,
            _ => 99
        };
    }

    public enum Priority { Low, Medium, High, Critical }

    public sealed class TaskDependency
    {
        public string FromId { get; init; } = "";
        public string ToId { get; init; } = "";
        public DependencyKind Kind { get; set; } = DependencyKind.Blocking;
    }

    public enum DependencyKind { Blocking, Satisfied, Optional }

    public sealed class TaskPlanResult
    {
        public PlanStatus Status { get; init; }
        public double Confidence { get; init; }
        public TaskPlan? Plan { get; init; }
        public PlanValidation Validation { get; init; } = new();
        public List<SkillRecommendation> NextRecommendations { get; init; } = new();
        public PlanMetadata? Metadata { get; init; }
    }

    public enum PlanStatus { Success, Partial, Failed }

    public sealed class PlanValidation
    {
        public bool IsValid { get; init; }
        public List<string> Issues { get; init; } = new();
        public int TaskCount { get; init; }
        public int DependencyCount { get; init; }
        public int EstimatedRounds { get; init; }
    }

    public sealed class PlanMetadata
    {
        public int TaskCount { get; init; }
        public int DependencyCount { get; init; }
        public double Confidence { get; init; }
        public string? PreviousRoundId { get; init; }
    }

    public sealed class SkillRecommendation
    {
        public string Skill { get; init; }
        public string Rationale { get; init; }

        public SkillRecommendation(string skill, string rationale)
        {
            Skill = skill;
            Rationale = rationale;
        }
    }

    public sealed class SkillRegistry
    {
        public string Name { get; init; }
        public string Description { get; init; }

        public SkillRegistry(string name, string description)
        {
            Name = name;
            Description = description;
        }
    }
}

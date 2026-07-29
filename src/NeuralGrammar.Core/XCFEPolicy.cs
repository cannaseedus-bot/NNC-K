using System;
using System.Collections.Generic;
using System.Linq;
using NeuralGrammar.Core;

namespace NeuralGrammar.Core.XCFE
{
    /// <summary>
    /// XCFE admission policy.
    ///
    /// Policy does not execute verbs. It proves that an operation is admitted
    /// under the current K'UHUL fold, lane, capability grants, determinism law,
    /// and runtime resource limits.
    /// </summary>
    public class XCFEPolicy
    {
        private Capability _grants = Capability.None;
        private readonly PolicyLimits _limits = new();
        private readonly NetworkPrivacyPolicy _networkPrivacy = new();

        public XCFEPolicy()
        {
        }

        public XCFEPolicy(Capability[] grants, PolicyLimits limits = null)
        {
            if (grants != null)
                foreach (var grant in grants)
                    _grants |= grant;

            if (limits != null)
                _limits.CopyFrom(limits);
        }

        public static XCFEPolicy FromManifest(Manifest manifest)
        {
            var policy = new XCFEPolicy();

            if (manifest?.Grants != null)
            {
                foreach (var grant in manifest.Grants)
                {
                    if (Enum.TryParse<Capability>(grant, true, out var cap))
                        policy._grants |= cap;
                }
            }

            if (manifest?.Limits != null)
                policy._limits.CopyFrom(manifest.Limits);

            if (manifest?.NetworkPrivacy != null)
                policy._networkPrivacy.CopyFrom(manifest.NetworkPrivacy);

            return policy;
        }

        public bool IsGranted(Capability cap)
        {
            if (cap == Capability.None) return true;
            return (_grants & cap) == cap;
        }

        /// <summary>
        /// Compatibility check: validates only verb existence and capability.
        /// Runtime execution should use AdmitVerb().
        /// </summary>
        public PolicyResult CheckVerb(string verb)
        {
            if (!XCFEStdlib.IsKnown(verb))
                return PolicyResult.Deny($"Unknown verb: {verb}");

            var required = XCFEStdlib.RequiredCapability(verb);
            if (!IsGranted(required))
                return PolicyResult.Deny(
                    $"Verb '{verb}' requires capability '{required}' which is not granted");

            return PolicyResult.Allow();
        }

        /// <summary>
        /// Full operation admission through Stdlib + FoldAlgebra + policy.
        /// </summary>
        public PolicyResult AdmitVerb(
            string verb,
            FoldAlgebra algebra,
            string lane = null,
            RuntimeUsage usage = null,
            bool hasSeed = false)
        {
            if (algebra == null)
                return PolicyResult.Deny("FoldAlgebra is required for XCFE admission");

            var limitCheck = CheckLimits(usage ?? new RuntimeUsage());
            if (!limitCheck.Allowed)
                return limitCheck;

            var admission = XCFEStdlib.Admit(verb, algebra, _grants, lane);
            if (!admission.Admitted)
                return PolicyResult.Deny(admission.Reason);

            var determinism = CheckDeterminism(
                admission.Deterministic ? "deterministic" : "nondet",
                hasSeed,
                admission.Replay);

            if (!determinism.Allowed)
                return determinism;

            return PolicyResult.Allow(admission);
        }

        public PolicyResult CheckLimits(
            int concurrency,
            int taskCount,
            int recursionDepth,
            long exprBytes)
        {
            return CheckLimits(new RuntimeUsage
            {
                Concurrency = concurrency,
                TaskCount = taskCount,
                RecursionDepth = recursionDepth,
                ExprBytes = exprBytes
            });
        }

        public PolicyResult CheckLimits(RuntimeUsage usage)
        {
            usage ??= new RuntimeUsage();

            if (_limits.MaxConcurrency > 0 &&
                usage.Concurrency > _limits.MaxConcurrency)
                return PolicyResult.Deny(
                    $"Concurrency {usage.Concurrency} exceeds limit {_limits.MaxConcurrency}");

            if (_limits.MaxTasks > 0 &&
                usage.TaskCount > _limits.MaxTasks)
                return PolicyResult.Deny(
                    $"Task count {usage.TaskCount} exceeds limit {_limits.MaxTasks}");

            if (_limits.MaxRecursion > 0 &&
                usage.RecursionDepth > _limits.MaxRecursion)
                return PolicyResult.Deny(
                    $"Recursion depth {usage.RecursionDepth} exceeds limit {_limits.MaxRecursion}");

            if (_limits.MaxExprBytes > 0 &&
                usage.ExprBytes > _limits.MaxExprBytes)
                return PolicyResult.Deny(
                    $"Expression size {usage.ExprBytes} exceeds limit {_limits.MaxExprBytes}");

            if (_limits.TimeoutMs > 0 &&
                usage.ElapsedMs > _limits.TimeoutMs)
                return PolicyResult.Deny(
                    $"Elapsed time {usage.ElapsedMs}ms exceeds timeout {_limits.TimeoutMs}ms");

            if (_limits.CpuMs > 0 &&
                usage.CpuMs > _limits.CpuMs)
                return PolicyResult.Deny(
                    $"CPU time {usage.CpuMs}ms exceeds limit {_limits.CpuMs}ms");

            if (_limits.GpuMs > 0 &&
                usage.GpuMs > _limits.GpuMs)
                return PolicyResult.Deny(
                    $"GPU time {usage.GpuMs}ms exceeds limit {_limits.GpuMs}ms");

            return PolicyResult.Allow();
        }

        public PolicyResult CheckDeterminism(
            string determinismClass,
            bool hasSeed)
        {
            return CheckDeterminism(
                determinismClass,
                hasSeed,
                ReplayLaw.Deterministic);
        }

        public PolicyResult CheckDeterminism(
            string determinismClass,
            bool hasSeed,
            ReplayLaw replayLaw)
        {
            var nondeterministic = string.Equals(
                determinismClass,
                "nondet",
                StringComparison.OrdinalIgnoreCase);

            if (!nondeterministic)
                return PolicyResult.Allow();

            // A nondeterministic pure computation can become reproducible with
            // an explicit seed. External/model results instead require evidence
            // capture so replay consumes the recorded result/mutation.
            if (hasSeed)
                return PolicyResult.Allow();

            if (replayLaw == ReplayLaw.RecordResult ||
                replayLaw == ReplayLaw.RecordMutation)
                return PolicyResult.Allow();

            return PolicyResult.Deny(
                "Nondeterministic operation requires an explicit seed or a replay evidence law");
        }

        /// <summary>
        /// Privacy/trust gate for Micronaut network delegation.
        /// This gate runs in addition to normal verb/capability/fold admission.
        /// </summary>
        public PolicyResult AdmitNetwork(
            PrivacyClass privacy,
            string remoteUserId = null,
            string remoteNodeId = null)
        {
            if (privacy == PrivacyClass.Private)
                return PolicyResult.Deny("Private operations are local-only");

            if (!_networkPrivacy.AllowRemoteExecution)
                return PolicyResult.Deny("Remote Micronaut execution is disabled");

            if (privacy == PrivacyClass.Trusted)
            {
                var trustedUser = !string.IsNullOrWhiteSpace(remoteUserId) &&
                    _networkPrivacy.TrustedUserIds.Contains(
                        remoteUserId, StringComparer.OrdinalIgnoreCase);

                var trustedNode = !string.IsNullOrWhiteSpace(remoteNodeId) &&
                    _networkPrivacy.TrustedNodeIds.Contains(
                        remoteNodeId, StringComparer.OrdinalIgnoreCase);

                if (!trustedUser && !trustedNode)
                    return PolicyResult.Deny(
                        "Trusted operation requires an approved buddy user or node");
            }

            if (privacy == PrivacyClass.Public && !_networkPrivacy.AllowPublicPool)
                return PolicyResult.Deny("Public Micronaut pool is disabled");

            return PolicyResult.Allow();
        }

        public XCFEPolicy SetNetworkPrivacy(NetworkPrivacyPolicy policy)
        {
            _networkPrivacy.CopyFrom(policy);
            return this;
        }

        public NetworkPrivacyPolicy NetworkPrivacy => _networkPrivacy.Clone();

        public XCFEPolicy Grant(Capability cap)
        {
            _grants |= cap;
            return this;
        }

        public XCFEPolicy GrantAll()
        {
            foreach (Capability cap in Enum.GetValues<Capability>())
                _grants |= cap;
            return this;
        }

        public XCFEPolicy Revoke(Capability cap)
        {
            _grants &= ~cap;
            return this;
        }

        public XCFEPolicy SetLimits(PolicyLimits limits)
        {
            _limits.CopyFrom(limits);
            return this;
        }

        public Capability GrantedCapabilities => _grants;

        public IReadOnlySet<Capability> Grants
        {
            get
            {
                var values = Enum.GetValues<Capability>()
                    .Where(cap =>
                        cap != Capability.None &&
                        IsGranted(cap))
                    .ToHashSet();

                return values;
            }
        }

        public PolicyLimits Limits => _limits.Clone();
    }

    public class RuntimeUsage
    {
        public int Concurrency { get; set; }
        public int TaskCount { get; set; }
        public int RecursionDepth { get; set; }
        public long ExprBytes { get; set; }
        public long ElapsedMs { get; set; }
        public long CpuMs { get; set; }
        public long GpuMs { get; set; }
    }

    public class PolicyLimits
    {
        public int MaxConcurrency { get; set; } = 0;
        public int MaxTasks { get; set; } = 0;
        public int MaxRecursion { get; set; } = 64;
        public long TimeoutMs { get; set; } = 30000;
        public long CpuMs { get; set; } = 0;
        public long GpuMs { get; set; } = 0;
        public long MaxExprBytes { get; set; } = 16384;

        public void CopyFrom(PolicyLimits other)
        {
            if (other == null) return;

            MaxConcurrency = other.MaxConcurrency;
            MaxTasks = other.MaxTasks;
            MaxRecursion = other.MaxRecursion;
            TimeoutMs = other.TimeoutMs;
            CpuMs = other.CpuMs;
            GpuMs = other.GpuMs;
            MaxExprBytes = other.MaxExprBytes;
        }

        public PolicyLimits Clone()
        {
            var clone = new PolicyLimits();
            clone.CopyFrom(this);
            return clone;
        }
    }

    public class PolicyResult
    {
        public bool Allowed { get; private set; }
        public string Reason { get; private set; }
        public VerbAdmission Admission { get; private set; }

        public static PolicyResult Allow(VerbAdmission admission = null) =>
            new PolicyResult
            {
                Allowed = true,
                Reason = null,
                Admission = admission
            };

        public static PolicyResult Deny(string reason) =>
            new PolicyResult
            {
                Allowed = false,
                Reason = reason,
                Admission = null
            };
    }

    public enum PrivacyClass
    {
        Private = 0,
        Trusted = 1,
        Public = 2
    }

    public class NetworkPrivacyPolicy
    {
        public bool AllowRemoteExecution { get; set; } = false;
        public bool AllowPublicPool { get; set; } = false;
        public List<string> TrustedUserIds { get; set; } = new();
        public List<string> TrustedNodeIds { get; set; } = new();

        public void CopyFrom(NetworkPrivacyPolicy other)
        {
            if (other == null) return;
            AllowRemoteExecution = other.AllowRemoteExecution;
            AllowPublicPool = other.AllowPublicPool;
            TrustedUserIds = other.TrustedUserIds?.Distinct(
                StringComparer.OrdinalIgnoreCase).ToList() ?? new();
            TrustedNodeIds = other.TrustedNodeIds?.Distinct(
                StringComparer.OrdinalIgnoreCase).ToList() ?? new();
        }

        public NetworkPrivacyPolicy Clone()
        {
            var clone = new NetworkPrivacyPolicy();
            clone.CopyFrom(this);
            return clone;
        }
    }

    public class Manifest
    {
        public string[] Grants { get; set; }
        public PolicyLimits Limits { get; set; }
        public NetworkPrivacyPolicy NetworkPrivacy { get; set; }
    }
}

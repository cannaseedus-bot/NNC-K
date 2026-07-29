using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace NeuralGrammar.Core
{
    public class SemanticDataset
    {
        private List<PhaseSequence> _phaseSeqs = new();
        private List<Conversation> _convs = new();
        private List<ProgramSample> _progs = new();
        private List<MathMLSample> _math = new();
        private List<Doc> _docs = new();

        public int TotalSamples => _phaseSeqs.Count + _convs.Count + _progs.Count + _math.Count + _docs.Count;
        public int PhaseSequenceCount => _phaseSeqs.Count;
        public int ConversationCount => _convs.Count;
        public int ProgramCount => _progs.Count;
        public int MathMLCount => _math.Count;
        public int DocumentCount => _docs.Count;

        // Load from JSON
        public void LoadPhaseSequences(string path) { var d = JsonSerializer.Deserialize<PhaseSeqData>(System.IO.File.ReadAllText(path)); _phaseSeqs = d.Samples.ToList(); }
        public void LoadConversations(string path) { var d = JsonSerializer.Deserialize<ConvData>(System.IO.File.ReadAllText(path)); _convs = d.Conversations.ToList(); }
        public void LoadPrograms(string path) { var d = JsonSerializer.Deserialize<ProgData>(System.IO.File.ReadAllText(path)); _progs = d.Programs.ToList(); }
        public void LoadMathML(string path) { var d = JsonSerializer.Deserialize<MathData>(System.IO.File.ReadAllText(path)); _math = d.Expressions.ToList(); }
        public void LoadDocuments(string path) { var d = JsonSerializer.Deserialize<DocData>(System.IO.File.ReadAllText(path)); _docs = d.Documents.ToList(); }

        public void AddPhaseSequence(PhaseSequence s) => _phaseSeqs.Add(s);
        public void AddConversation(Conversation c) => _convs.Add(c);
        public void AddProgram(ProgramSample p) => _progs.Add(p);
        public void AddMathML(MathMLSample m) => _math.Add(m);
        public void AddDocument(Doc d) => _docs.Add(d);

        public void AddFromChatHistory(string userMsg, string assistantMsg, string intent)
        {
            AddConversation(new Conversation { User = userMsg, Assistant = assistantMsg, Intent = intent, Phases = new[] { "Pop", "Wo", "Yax", "Sek", "Chen", "Xul" } });
            AddPhaseSequence(new PhaseSequence
            {
                Sequence = new List<PhaseValue>
                {
                    new PhaseValue { Phase = FoldPhase.Pop, Value = userMsg },
                    new PhaseValue { Phase = FoldPhase.Wo, Value = Tokenize(userMsg).FirstOrDefault() ?? "" },
                    new PhaseValue { Phase = FoldPhase.Yax, Value = intent },
                    new PhaseValue { Phase = FoldPhase.Sek, Value = assistantMsg },
                    new PhaseValue { Phase = FoldPhase.Chen, Value = "refined" },
                    new PhaseValue { Phase = FoldPhase.Xul, Value = "complete" }
                },
                Label = intent
            });
        }

        // Prepare training data — maps all samples to (input, expected) NDArray pairs
        public (List<NDArray> inputs, List<NDArray> expected) Prepare()
        {
            var inputs = new List<NDArray>();
            var expected = new List<NDArray>();

            // Phase sequences → phase transition pairs
            foreach (var seq in _phaseSeqs)
                for (int i = 0; i < seq.Sequence.Count - 1; i++)
                { inputs.Add(TensorizePhase(seq.Sequence[i])); expected.Add(TensorizePhase(seq.Sequence[i + 1])); }

            // Conversations → intent embeddings
            foreach (var c in _convs)
            { inputs.Add(TensorizeText(c.User)); expected.Add(IntentVec(c.Intent)); }

            // Programs → code embeddings
            foreach (var p in _progs)
            { inputs.Add(TensorizeText(p.Code)); expected.Add(SemVec(p.Semantic)); }

            // MathML → char embeddings
            foreach (var m in _math)
            { inputs.Add(TensorizeText(m.MathML)); expected.Add(SemVec(m.Semantic)); }

            // Documents → text embeddings
            foreach (var d in _docs)
            { inputs.Add(TensorizeText(d.Text)); expected.Add(new NDArray(d.Embeddings, 1, d.Embeddings.Length)); }

            return (inputs, expected);
        }

        // === TENSORIZATION ===
        private NDArray TensorizePhase(PhaseValue pv)
        {
            var oh = new double[8];
            oh[(int)pv.Phase] = 1.0;
            if (!string.IsNullOrEmpty(pv.Value))
                oh[7] = (pv.Value.GetHashCode() % 1000) / 1000.0;
            return new NDArray(oh, 1, 8);
        }

        private NDArray TensorizeText(string text)
        {
            var tokens = Tokenize(text);
            var v = new double[64];
            for (int i = 0; i < Math.Min(tokens.Length, 64); i++)
                v[i] = (tokens[i].GetHashCode() % 10000) / 10000.0;
            return new NDArray(v, 1, 64);
        }

        private NDArray IntentVec(string intent)
        {
            var v = new double[16];
            var h = intent.GetHashCode();
            for (int i = 0; i < 16; i++) v[i] = ((h >> i) & 1) > 0 ? 1.0 : 0.0;
            return new NDArray(v, 1, 16);
        }

        private NDArray SemVec(string sem)
        {
            var v = new double[8];
            var h = sem.GetHashCode();
            for (int i = 0; i < 8; i++) v[i] = ((h >> i) & 1) > 0 ? 1.0 : 0.0;
            return new NDArray(v, 1, 8);
        }

        private string[] Tokenize(string t) => t.Split(new[] { ' ', '\n', '\r', '\t', '.', ',', ';', '(', ')', '[', ']', '{', '}', '"', '\'', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);

        // === GENERATE SAMPLE DATA ===
        public static string GenerateSampleJson()
        {
            var data = new
            {
                phase_sequences = new[] {
                    new { sequence = new[] { new { phase = "Pop", value = "user query" }, new { phase = "Wo", value = "normalized" }, new { phase = "Yax", value = "plan" }, new { phase = "Sek", value = "exec" }, new { phase = "Chen", value = "refine" }, new { phase = "Xul", value = "answer" } }, label = "search" },
                    new { sequence = new[] { new { phase = "Pop", value = "2+2" }, new { phase = "Wo", value = "2+2" }, new { phase = "Yax", value = "compute" }, new { phase = "Sek", value = "4" }, new { phase = "Chen", value = "4" }, new { phase = "Xul", value = "4" } }, label = "math" }
                },
                conversations = new[] {
                    new { user = "What is the weather?", assistant = "Checking...", phases = new[] { "Pop", "Wo", "Yax", "Sek", "Chen", "Xul" }, intent = "weather" },
                    new { user = "Tell me a joke", assistant = "Why did the chicken cross the road?", phases = new[] { "Pop", "Wo", "Yax", "Sek", "Chen", "Xul" }, intent = "humor" }
                },
                programs = new[] {
                    new { code = "function add(a,b){return a+b;}", phases = new[] { "Pop", "Wo", "Yax", "Sek", "Xul" }, semantic = "arithmetic" }
                },
                mathml = new[] {
                    new { mathml = "<apply><sin/><ci>x</ci></apply>", phases = new[] { "Pop", "Wo", "Yax", "Sek", "Xul" }, semantic = "trig" }
                },
                documents = new[] {
                    new { text = "Artificial Intelligence simulates human intelligence", tokens = new[] { "AI", "intelligence" }, embeddings = new[] { 0.1, 0.2, 0.3, 0.4, 0.5 }, phases = new[] { "Pop", "Wo", "Yax", "Sek", "Chen", "Xul" } }
                }
            };
            return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        }

        // === DATA CLASSES ===
        public class PhaseSeqData { public List<PhaseSequence> Samples { get; set; } }
        public class PhaseSequence { public List<PhaseValue> Sequence { get; set; } public string Label { get; set; } }
        public class PhaseValue { public FoldPhase Phase { get; set; } public string Value { get; set; } }
        public class ConvData { public List<Conversation> Conversations { get; set; } }
        public class Conversation { public string User { get; set; } public string Assistant { get; set; } public string[] Phases { get; set; } public string Intent { get; set; } }
        public class ProgData { public List<ProgramSample> Programs { get; set; } }
        public class ProgramSample { public string Code { get; set; } public string[] Phases { get; set; } public string Semantic { get; set; } }
        public class MathData { public List<MathMLSample> Expressions { get; set; } }
        public class MathMLSample { public string MathML { get; set; } public string[] Phases { get; set; } public string Semantic { get; set; } }
        public class DocData { public List<Doc> Documents { get; set; } }
        public class Doc { public string Text { get; set; } public string[] Tokens { get; set; } public double[] Embeddings { get; set; } public string[] Phases { get; set; } }
        
    }
}

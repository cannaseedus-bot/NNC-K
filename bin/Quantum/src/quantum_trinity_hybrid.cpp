// quantum_trinity_hybrid.cpp
// Roslyn + RegEx + ELIZA + ADAM12 Integration
// Version: 9.0

#include "json.hpp"
using json = nlohmann::json;

#include <string>
#include <vector>
#include <map>
#include <unordered_map>
#include <regex>
#include <chrono>
#include <thread>
#include <mutex>
#include <algorithm>
#include <cmath>
#include <random>
#include <fstream>
#include <sstream>
#include <queue>
#include <set>
#include <memory>
#include <functional>
#include <iostream>
#include <io.h>
#include <stdio.h>

#ifdef _WIN32
#define NOMINMAX
#include <windows.h>
#include <winhttp.h>
#pragma comment(lib, "winhttp.lib")
#endif

bool g_quiet = false;

#ifdef _WIN32
bool is_interactive() {
    return _isatty(_fileno(stdin)) != 0 && _isatty(_fileno(stdout)) != 0;
}
#else
bool is_interactive() { return false; }
#endif

// ============================================================
// ROSLYN PARSER ENGINE
// ============================================================

class RoslynParser {
private:
    struct SyntaxNode {
        std::string type;
        std::string value;
        std::vector<std::shared_ptr<SyntaxNode>> children;
        std::map<std::string, std::string> attributes;
        int start_line = 0;
        int end_line = 0;
        int start_column = 0;
        int end_column = 0;
    };

    struct SemanticModel {
        std::map<std::string, std::string> symbols;
        std::map<std::string, std::vector<std::string>> references;
        std::map<std::string, std::string> types;
        std::map<std::string, std::vector<std::string>> method_calls;
        std::map<std::string, std::string> variable_types;
    };

    std::map<std::string, std::regex> pattern_cache;
    std::shared_ptr<SyntaxNode> ast_root;
    SemanticModel semantic_model;

public:
    RoslynParser() {
        initialize_patterns();
    }

    void initialize_patterns() {
        pattern_cache["class"] = std::regex(R"(class\s+(\w+)(?:\s*:\s*(\w+))?\s*\{)");
        pattern_cache["method"] = std::regex(R"((public|private|protected|internal)\s+(static\s+)?(\w+)\s+(\w+)\s*\(([^)]*)\)\s*\{)");
        pattern_cache["property"] = std::regex(R"((public|private|protected|internal)\s+(\w+)\s+(\w+)\s*\{\s*get\s*;\s*set\s*;\s*\})");
        pattern_cache["variable"] = std::regex(R"((var|int|string|bool|double|float|long|short|byte|char)\s+(\w+)\s*=\s*([^;]+);)");
        pattern_cache["comment"] = std::regex(R"((\/\/.*)|(\/\*[\s\S]*?\*\/))");
        pattern_cache["namespace"] = std::regex(R"(namespace\s+(\w+)(?:\.(\w+))?\s*\{)");
        pattern_cache["interface"] = std::regex(R"(interface\s+(\w+)\s*\{)");
        pattern_cache["enum"] = std::regex(R"(enum\s+(\w+)\s*\{([^}]*)\})");
        pattern_cache["delegate"] = std::regex(R"(delegate\s+(\w+)\s+(\w+)\s*\(([^)]*)\);)");
        pattern_cache["event"] = std::regex(R"(event\s+(\w+)\s+(\w+)\s*;)");
        pattern_cache["struct"] = std::regex(R"(struct\s+(\w+)\s*\{)");
        pattern_cache["record"] = std::regex(R"(record\s+(\w+)(?:\s*\([^)]*\))?\s*;)");
        pattern_cache["lambda"] = std::regex(R"((\w+)\s*=>\s*\{?([^;]*)\}?;)");
        pattern_cache["linq"] = std::regex(R"(from\s+(\w+)\s+in\s+(\w+)\s+where\s+([^\s]+)\s+select\s+([^\s]+))");
    }

    std::shared_ptr<SyntaxNode> parse_code(const std::string& code) {
        ast_root = std::make_shared<SyntaxNode>();
        ast_root->type = "CompilationUnit";
        ast_root->value = "";

        std::vector<std::string> lines = split_lines(code);
        std::map<std::string, std::shared_ptr<SyntaxNode>> namespace_nodes;
        std::map<std::string, std::shared_ptr<SyntaxNode>> class_nodes;

        for (size_t i = 0; i < lines.size(); i++) {
            std::string line = lines[i];

            std::smatch comment_match;
            if (std::regex_search(line, comment_match, pattern_cache["comment"])) {
                auto comment_node = std::make_shared<SyntaxNode>();
                comment_node->type = "Comment";
                comment_node->value = comment_match[0].str();
                comment_node->start_line = (int)i + 1;
                ast_root->children.push_back(comment_node);
                continue;
            }

            std::smatch ns_match;
            if (std::regex_search(line, ns_match, pattern_cache["namespace"])) {
                auto ns_node = std::make_shared<SyntaxNode>();
                ns_node->type = "Namespace";
                ns_node->value = ns_match[1].str();
                ns_node->start_line = (int)i + 1;
                if (ns_match.size() > 2) {
                    ns_node->attributes["parent"] = ns_match[2].str();
                }
                namespace_nodes[ns_node->value] = ns_node;
                ast_root->children.push_back(ns_node);
                continue;
            }

            std::smatch class_match;
            if (std::regex_search(line, class_match, pattern_cache["class"])) {
                auto class_node = std::make_shared<SyntaxNode>();
                class_node->type = "Class";
                class_node->value = class_match[1].str();
                class_node->start_line = (int)i + 1;
                if (class_match.size() > 2 && !class_match[2].str().empty()) {
                    class_node->attributes["base"] = class_match[2].str();
                }
                class_nodes[class_node->value] = class_node;
                ast_root->children.push_back(class_node);
                continue;
            }

            std::smatch method_match;
            if (std::regex_search(line, method_match, pattern_cache["method"])) {
                auto method_node = std::make_shared<SyntaxNode>();
                method_node->type = "Method";
                method_node->value = method_match[4].str();           // method name (was [5]=params)
                method_node->attributes["access"] = method_match[1].str();
                method_node->attributes["return_type"] = method_match[3].str();  // was [4]=name
                method_node->attributes["parameters"] = method_match[5].str();   // was [6]=out-of-range
                method_node->start_line = (int)i + 1;

                if (!class_nodes.empty()) {
                    auto last_class = class_nodes.rbegin()->second;
                    last_class->children.push_back(method_node);
                } else {
                    ast_root->children.push_back(method_node);
                }
                continue;
            }

            std::smatch prop_match;
            if (std::regex_search(line, prop_match, pattern_cache["property"])) {
                auto prop_node = std::make_shared<SyntaxNode>();
                prop_node->type = "Property";
                prop_node->value = prop_match[3].str();
                prop_node->attributes["access"] = prop_match[1].str();
                prop_node->attributes["type"] = prop_match[2].str();
                prop_node->start_line = (int)i + 1;

                if (!class_nodes.empty()) {
                    auto last_class = class_nodes.rbegin()->second;
                    last_class->children.push_back(prop_node);
                }
                continue;
            }

            std::smatch var_match;
            if (std::regex_search(line, var_match, pattern_cache["variable"])) {
                auto var_node = std::make_shared<SyntaxNode>();
                var_node->type = "Variable";
                var_node->value = var_match[2].str();
                var_node->attributes["type"] = var_match[1].str();
                var_node->attributes["value"] = var_match[3].str();
                var_node->start_line = (int)i + 1;
                ast_root->children.push_back(var_node);
                continue;
            }

            std::smatch linq_match;
            if (std::regex_search(line, linq_match, pattern_cache["linq"])) {
                auto linq_node = std::make_shared<SyntaxNode>();
                linq_node->type = "LinqQuery";
                linq_node->value = linq_match[0].str();
                linq_node->attributes["from"] = linq_match[1].str();
                linq_node->attributes["in"] = linq_match[2].str();
                linq_node->attributes["where"] = linq_match[3].str();
                linq_node->attributes["select"] = linq_match[4].str();
                linq_node->start_line = (int)i + 1;
                ast_root->children.push_back(linq_node);
                continue;
            }

            std::smatch lambda_match;
            if (std::regex_search(line, lambda_match, pattern_cache["lambda"])) {
                auto lambda_node = std::make_shared<SyntaxNode>();
                lambda_node->type = "Lambda";
                lambda_node->value = lambda_match[0].str();
                lambda_node->attributes["parameter"] = lambda_match[1].str();
                lambda_node->attributes["body"] = lambda_match[2].str();
                lambda_node->start_line = (int)i + 1;
                ast_root->children.push_back(lambda_node);
                continue;
            }
        }

        build_semantic_model();
        return ast_root;
    }

    std::shared_ptr<SyntaxNode> get_ast() {
        return ast_root;
    }

    std::map<std::string, std::string> analyze_code(const std::string& code) {
        std::map<std::string, std::string> analysis;
        int class_count = 0, method_count = 0, property_count = 0;
        int variable_count = 0, comment_count = 0, namespace_count = 0;

        std::istringstream iss(code);
        std::string line;
        while (std::getline(iss, line)) {
            if (std::regex_search(line, pattern_cache["class"])) class_count++;
            if (std::regex_search(line, pattern_cache["method"])) method_count++;
            if (std::regex_search(line, pattern_cache["property"])) property_count++;
            if (std::regex_search(line, pattern_cache["variable"])) variable_count++;
            if (std::regex_search(line, pattern_cache["comment"])) comment_count++;
            if (std::regex_search(line, pattern_cache["namespace"])) namespace_count++;
        }

        analysis["classes"] = std::to_string(class_count);
        analysis["methods"] = std::to_string(method_count);
        analysis["properties"] = std::to_string(property_count);
        analysis["variables"] = std::to_string(variable_count);
        analysis["comments"] = std::to_string(comment_count);
        analysis["namespaces"] = std::to_string(namespace_count);
        analysis["total_lines"] = std::to_string((int)split_lines(code).size());

        return analysis;
    }

    // Emits structural edges {source,relation,target} from the parsed AST. The regexes already
    // capture base classes / namespace nesting / member types; this surfaces them for CHEESE.
    json extract_relations(const std::string& code) {
        parse_code(code);
        json edges = json::array();
        walk_relations(ast_root, "", edges);
        return edges;
    }

private:
    void walk_relations(const std::shared_ptr<SyntaxNode>& node, const std::string& scope, json& edges) {
        if (!node) return;
        if (node->type == "Namespace") {
            auto it = node->attributes.find("parent");
            if (it != node->attributes.end() && !it->second.empty())
                edges.push_back({{"source", node->value}, {"relation", "contains"}, {"target", it->second}});
        } else if (node->type == "Class") {
            auto it = node->attributes.find("base");
            if (it != node->attributes.end() && !it->second.empty())
                edges.push_back({{"source", node->value}, {"relation", "inherits"}, {"target", it->second}});
        } else if (node->type == "Method") {
            if (!scope.empty())
                edges.push_back({{"source", scope}, {"relation", "declares"}, {"target", node->value}});
            auto it = node->attributes.find("return_type");
            if (it != node->attributes.end() && !it->second.empty())
                edges.push_back({{"source", node->value}, {"relation", "returns"}, {"target", it->second}});
        } else if (node->type == "Property") {
            if (!scope.empty())
                edges.push_back({{"source", scope}, {"relation", "declares"}, {"target", node->value}});
            auto it = node->attributes.find("type");
            if (it != node->attributes.end() && !it->second.empty())
                edges.push_back({{"source", node->value}, {"relation", "typed"}, {"target", it->second}});
        } else if (node->type == "Variable") {
            auto it = node->attributes.find("type");
            if (it != node->attributes.end() && !it->second.empty())
                edges.push_back({{"source", node->value}, {"relation", "typed"}, {"target", it->second}});
        }
        std::string childScope = (node->type == "Class") ? node->value : scope;
        for (const auto& c : node->children) walk_relations(c, childScope, edges);
    }

    std::vector<std::string> split_lines(const std::string& text) {
        std::vector<std::string> lines;
        std::istringstream iss(text);
        std::string line;
        while (std::getline(iss, line)) {
            lines.push_back(line);
        }
        return lines;
    }

    void build_semantic_model() {
        for (const auto& node : ast_root->children) {
            if (node->type == "Class") {
                semantic_model.types[node->value] = "class";
                for (const auto& child : node->children) {
                    if (child->type == "Method") {
                        semantic_model.method_calls[node->value].push_back(child->value);
                    }
                    if (child->type == "Property") {
                        semantic_model.symbols[child->value] = child->attributes["type"];
                    }
                }
            }
            if (node->type == "Variable") {
                semantic_model.variable_types[node->value] = node->attributes["type"];
            }
        }
    }
};

// ============================================================
// REGEX ENGINE WITH PATTERN COMPILATION
// ============================================================

class RegexEngine {
private:
    std::map<std::string, std::regex> pattern_registry;
    std::map<std::string, std::function<void(const std::smatch&)>> pattern_handlers;
    std::map<std::string, std::vector<std::string>> pattern_groups;
    std::vector<std::string> pattern_history;

public:
    RegexEngine() {
        initialize_patterns();
    }

    void initialize_patterns() {
        add_pattern("greeting", R"((hi|hello|hey|howdy|greetings)\s*!?)");
        add_pattern("question", R"((what|why|how|when|where|who|which|does|is|are|do|did)\s+([^?]+)\?)");
        add_pattern("emotion", R"((happy|sad|angry|excited|frustrated|anxious|calm|confused|curious)\s*(?:about|with|by)?\s*([^.]*))");
        add_pattern("agreement", R"((yes|yeah|yep|sure|absolutely|definitely|correct|right)\s*!?)");
        add_pattern("disagreement", R"((no|nope|nah|not|never|impossible|unlikely)\s*!?)");
        add_pattern("request", R"((can|could|would|will|may|might)\s+you\s+([^?]+)\?)");
        add_pattern("command", R"((please\s+)?(tell|show|give|make|do|create|find|search|run|execute)\s+([^.!]+))");
        add_pattern("programming", R"((function|method|class|interface|enum|struct|delegate|event)\s+(\w+))");
        add_pattern("quantum", R"((quantum|superposition|entanglement|coherence|decoherence|collapse|wave|particle)\s*([^.]*))");
        add_pattern("emotion_intensity", R"((very|extremely|really|quite|somewhat|slightly|barely)\s+(happy|sad|angry|excited|frustrated))");
        add_pattern("time_reference", R"((today|tomorrow|yesterday|now|soon|later|eventually|never|always|sometimes|usually)\s*([^.]*))");
        add_pattern("need", R"((need|want|require|must|have to|should)\s+(to\s+)?([^.]+))");
        add_pattern("ability", R"((can|could|able to|capable of)\s+([^.]+))");
        add_pattern("identifier", R"(([a-zA-Z_][a-zA-Z0-9_]*))");
        add_pattern("number", R"((\d+\.?\d*))");
        add_pattern("string_literal", R"((["'])([^"']*)\1)");
        add_pattern("url", R"((https?://[^\s]+))");
        add_pattern("email", R"(([a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}))");
        add_pattern("file_path", R"(([a-zA-Z]:\\(?:[^\\/:*?"<>>|\r\n]+\\)*[^\\/:*?"<>>|\r\n]*))");
        add_pattern("date_time", R"((\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}))");
        add_pattern("guid", R"(([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}))");
        add_pattern("credential", R"((password|token|key|secret|apikey|auth)\s*[:=]\s*([^\s]+))");
        add_pattern("sensitive", R"((ssn|creditcard|bank|account|security|private|confidential)\s*:\s*([^\n]+))");
        add_pattern("code_block", R"((```\w*\n[\s\S]*?```))");
        add_pattern("inline_code", R"((`[^`]+`))");
        add_pattern("html_tag", R"((<[^>]+>))");
        add_pattern("json_object", R"((\{[^{}]*\}))");
        add_pattern("json_array", R"((\[[^\[\]]*\]))");
    }

    void add_pattern(const std::string& name, const std::string& pattern) {
        pattern_registry[name] = std::regex(pattern, std::regex::icase | std::regex::optimize);
        pattern_groups[name] = std::vector<std::string>();
        pattern_history.push_back(name);
    }

    void add_handler(const std::string& pattern_name,
                    std::function<void(const std::smatch&)> handler) {
        pattern_handlers[pattern_name] = handler;
    }

    std::map<std::string, std::vector<std::string>> match_all(const std::string& text) {
        std::map<std::string, std::vector<std::string>> results;

        for (const auto& [name, pattern] : pattern_registry) {
            std::smatch matches;
            std::string search_text = text;

            while (std::regex_search(search_text, matches, pattern)) {
                std::vector<std::string> match_values;
                for (size_t i = 0; i < matches.size(); i++) {
                    match_values.push_back(matches[i].str());
                }
                results[name] = match_values;

                if (pattern_handlers.find(name) != pattern_handlers.end()) {
                    pattern_handlers[name](matches);
                }

                if (pattern_groups.find(name) != pattern_groups.end()) {
                    for (size_t i = 1; i < matches.size(); i++) {
                        pattern_groups[name].push_back(matches[i].str());
                    }
                }

                search_text = matches.suffix().str();
            }
        }

        return results;
    }

    std::vector<std::string> extract_group(const std::string& pattern_name, int group_index = 1) {
        if (pattern_groups.find(pattern_name) != pattern_groups.end()) {
            if ((size_t)group_index < pattern_groups[pattern_name].size()) {
                return {pattern_groups[pattern_name][group_index]};
            }
        }
        return {};
    }

    std::map<std::string, std::vector<std::string>> get_all_matches(const std::string& text) {
        std::map<std::string, std::vector<std::string>> all_matches;

        for (const auto& [name, pattern] : pattern_registry) {
            std::smatch matches;
            std::string search_text = text;
            std::vector<std::string> matched_texts;

            while (std::regex_search(search_text, matches, pattern)) {
                matched_texts.push_back(matches[0].str());
                search_text = matches.suffix().str();
            }

            if (!matched_texts.empty()) {
                all_matches[name] = matched_texts;
            }
        }

        return all_matches;
    }

    bool matches(const std::string& text, const std::string& pattern_name) {
        if (pattern_registry.find(pattern_name) != pattern_registry.end()) {
            return std::regex_search(text, pattern_registry[pattern_name]);
        }
        return false;
    }

    std::string replace(const std::string& text,
                       const std::string& pattern_name,
                       const std::string& replacement) {
        if (pattern_registry.find(pattern_name) != pattern_registry.end()) {
            return std::regex_replace(text, pattern_registry[pattern_name], replacement);
        }
        return text;
    }
};

// ============================================================
// ELIZA THERAPIST ENGINE
// ============================================================

class ElizaEngine {
private:
    struct Rule {
        std::string pattern;
        std::vector<std::string> responses;
        std::string memory_key;
        float weight;
    };

    struct Response {
        std::string text;
        float confidence = 0.0f;
        std::vector<std::string> triggers;
        std::string emotion;
    };

    std::vector<Rule> rules;
    std::map<std::string, std::string> memory;
    std::map<std::string, std::vector<std::string>> context_history;
    std::vector<std::string> user_history;
    std::map<std::string, float> emotional_state;
    std::mt19937 rng;

public:
    ElizaEngine() {
        rng.seed(std::chrono::system_clock::now().time_since_epoch().count());
        initialize_rules();
        initialize_emotional_state();
    }

    void initialize_rules() {
        rules = {
            {"(i am|i'm) (happy|glad|great|wonderful)",
             {"Why are you feeling happy?", "What made you feel this way?",
              "That's wonderful to hear!", "Tell me more about your happiness."},
             "happiness", 1.0f},
            {"(i am|i'm) (sad|depressed|down|unhappy)",
             {"I'm sorry to hear that. Can you tell me why?",
              "What's been bothering you?", "How long have you been feeling this way?",
              "I'm here to listen. What happened?"},
             "sadness", 1.0f},
            {"(i am|i'm) (angry|frustrated|mad|annoyed)",
             {"It sounds like you're angry. What happened?",
              "Anger can be a difficult emotion. Can you explain?",
              "Let's talk about what's making you frustrated."},
             "anger", 1.0f},
            {"(i am|i'm) (confused|lost|unsure|uncertain)",
             {"What's confusing you?", "Let me help you understand.",
              "It's okay to be confused. What specifically?"},
             "confusion", 0.9f},
            {"(i am|i'm) (excited|eager|enthusiastic|pumped)",
             {"Your excitement is contagious! What's happening?",
              "Tell me more about what's exciting you.",
              "I love hearing about things that excite people!"},
             "excitement", 0.8f},
            {"(i (?:love|like) (?:you|this|it))",
             {"I appreciate that.", "Thank you for sharing.",
              "It's nice to hear that.", "I'm glad you feel that way."},
             "affection", 0.6f},
            {"(you (?:are|were) (?:right|wrong|correct|incorrect))",
             {"Tell me more about why you think that.",
              "What makes you say that?", "I see. Interesting perspective."},
             "opinion", 0.5f},
            {"(my (?:mother|father|parent|sibling|brother|sister|family))",
             {"Tell me about your family.",
              "How do you feel about your family?",
              "Family relationships are important. Tell me more."},
             "family", 0.8f},
            {"(my (?:friend|partner|spouse|girlfriend|boyfriend))",
             {"Tell me about your relationship.", "How's that going for you?",
              "Relationships can be complex. What's on your mind?"},
             "relationship", 0.8f},
            {"(my (?:job|work|career|boss|colleague|team))",
             {"How is work going?", "Tell me about your professional life.",
              "Work-life balance is important. What's happening?"},
             "career", 0.7f},
            {"(i (?:want|need|wish) (?:to|that|i could))",
             {"What would that accomplish?", "Why do you want that?",
              "What's stopping you?", "That's an interesting desire."},
             "desire", 0.7f},
            {"(you (?:make|made) me (?:feel|think|realize))",
             {"I'm glad I could help.", "How does that make you feel?",
              "That's meaningful to hear."},
             "reflection", 0.5f},
            {"(i (?:think|believe|feel|consider) that)",
             {"What makes you think that?",
              "That's an interesting perspective.",
              "Tell me more about your thinking."},
             "belief", 0.6f},
            {"(do you remember|remember when)",
             {"I recall something about that.",
              "My memory isn't perfect, but tell me more.",
              "I remember you mentioned something similar before."},
             "memory", 0.5f},
            {"(i (?:can't|cannot) (?:do|handle|manage|deal with))",
             {"What's making it difficult?",
              "Is there another way to approach this?",
              "Sometimes we need help. Would you like support?"},
             "challenge", 0.7f},
            {"(i (?:did|made|accomplished|achieved) (?:it|that))",
             {"Congratulations! That's a great achievement.",
              "You should be proud of yourself.",
              "Tell me about your success."},
             "achievement", 0.6f}
        };
    }

    void initialize_emotional_state() {
        emotional_state = {
            {"happiness", 0.5f},
            {"sadness", 0.3f},
            {"anger", 0.2f},
            {"fear", 0.2f},
            {"surprise", 0.3f},
            {"trust", 0.5f},
            {"anticipation", 0.4f},
            {"disgust", 0.1f}
        };
    }

    Response process_input(const std::string& input) {
        Response response;
        response.confidence = 0.0f;
        response.emotion = "neutral";

        user_history.push_back(input);
        if (user_history.size() > 100) {
            user_history.erase(user_history.begin());
        }

        Rule* best_rule = nullptr;
        float best_weight = 0.0f;

        for (auto& rule : rules) {
            std::regex pattern(rule.pattern, std::regex::icase);
            if (std::regex_search(input, pattern)) {
                std::uniform_real_distribution<float> dist(0.0f, 0.1f);
                float weight = rule.weight * (1.0f + dist(rng));
                if (weight > best_weight) {
                    best_weight = weight;
                    best_rule = &rule;
                }
            }
        }

        if (best_rule != nullptr) {
            std::uniform_int_distribution<int> dist(0, (int)best_rule->responses.size() - 1);
            response.text = best_rule->responses[dist(rng)];
            response.confidence = best_weight;

            if (!best_rule->memory_key.empty()) {
                memory[best_rule->memory_key] = input;
            }

            if (best_rule->memory_key == "happiness") {
                response.emotion = "joy";
                emotional_state["happiness"] = std::min(1.0f, emotional_state["happiness"] + 0.1f);
            } else if (best_rule->memory_key == "sadness") {
                response.emotion = "sadness";
                emotional_state["sadness"] = std::min(1.0f, emotional_state["sadness"] + 0.1f);
            } else if (best_rule->memory_key == "anger") {
                response.emotion = "anger";
                emotional_state["anger"] = std::min(1.0f, emotional_state["anger"] + 0.1f);
            } else if (best_rule->memory_key == "excitement") {
                response.emotion = "excitement";
                emotional_state["anticipation"] = std::min(1.0f, emotional_state["anticipation"] + 0.1f);
            }

            if (memory.find("name") != memory.end()) {
                response.text += " " + memory["name"] + "?";
            }
        } else {
            std::vector<std::string> default_responses = {
                "Tell me more about that.",
                "I see. What else?",
                "That's interesting. Go on.",
                "How do you feel about that?",
                "What's on your mind?",
                "Could you elaborate?",
                "Why do you think that?",
                "I'm listening. Please continue."
            };
            std::uniform_int_distribution<int> dist(0, (int)default_responses.size() - 1);
            response.text = default_responses[dist(rng)];
            response.confidence = 0.3f;

            if (input.find('?') != std::string::npos) {
                response.text = "That's a good question. " + response.text;
            }
        }

        if (!context_history[response.emotion].empty()) {
            response.text += " Based on what you said before...";
        }

        if (response.emotion == "sadness" || response.emotion == "anger") {
            response.text += " I'm here to listen.";
        }

        return response;
    }

    void learn(const std::string& user_input, const std::string& response) {
        bool found_pattern = false;
        for (const auto& rule : rules) {
            std::regex pattern(rule.pattern, std::regex::icase);
            if (std::regex_search(user_input, pattern)) {
                found_pattern = true;
                break;
            }
        }

        if (!found_pattern && user_input.length() > 20) {
            std::vector<std::string> words = split_words(user_input);
            if (words.size() > 3) {
                std::string pattern = words[0] + " " + words[1] + " " + words[2];
                rules.push_back({pattern, {"That's interesting. Tell me more.",
                                          "I understand. Could you elaborate?"},
                                "custom", 0.5f});
            }
        }
    }

    void set_memory(const std::string& key, const std::string& value) {
        memory[key] = value;
    }

    std::map<std::string, float> get_emotional_state() {
        return emotional_state;
    }

private:
    std::vector<std::string> split_words(const std::string& text) {
        std::vector<std::string> words;
        std::stringstream ss(text);
        std::string word;
        while (ss >> word) {
            words.push_back(word);
        }
        return words;
    }
};

// ============================================================
// ADAM12 COGNITIVE ENGINE
// ============================================================

class Adam12Engine {
private:
    struct NeuralNode {
        std::string id;
        std::string type;
        float activation = 0.0f;
        float threshold = 0.0f;
        std::map<std::string, float> weights;
        std::map<std::string, float> input_values;
        std::map<std::string, float> output_values;
        std::string function;
        std::vector<std::string> connected_to;
        float bias = 0.0f;
        float learning_rate = 0.01f;
    };

    struct CognitivePattern {
        std::string name;
        std::vector<std::string> triggers;
        std::vector<std::string> responses;
        float priority = 0.0f;
        std::map<std::string, float> context;
        std::map<std::string, float> emotional_context;
        int frequency = 0;
        float strength = 0.0f;
    };

    struct DecisionFramework {
        std::string name;
        std::map<std::string, float> criteria;
        std::map<std::string, std::string> actions;
        std::map<std::string, float> weights;
        std::map<std::string, std::function<float(const std::map<std::string, float>&)>> evaluators;
    };

    std::map<std::string, NeuralNode> neural_network;
    std::map<std::string, CognitivePattern> cognitive_patterns;
    std::map<std::string, DecisionFramework> decision_frameworks;
    std::map<std::string, float> attention_focus;
    std::map<std::string, float> working_memory;
    std::vector<std::string> recent_actions;
    std::map<std::string, float> belief_state;
    std::map<std::string, float> goal_state;
    float cognitive_load = 0.0f;
    int iteration_count = 0;

public:
    Adam12Engine() {
        initialize_neural_network();
        initialize_cognitive_patterns();
        initialize_decision_frameworks();
    }

    void initialize_neural_network() {
        add_node("input_audio", "input", 0.0f, 0.3f, "linear");
        add_node("input_visual", "input", 0.0f, 0.3f, "linear");
        add_node("input_text", "input", 0.0f, 0.3f, "linear");
        add_node("input_emotional", "input", 0.0f, 0.3f, "linear");
        add_node("processing_audio", "hidden", 0.0f, 0.5f, "relu");
        add_node("processing_visual", "hidden", 0.0f, 0.5f, "relu");
        add_node("processing_text", "hidden", 0.0f, 0.5f, "relu");
        add_node("processing_emotional", "hidden", 0.0f, 0.5f, "relu");
        add_node("integration_sensory", "hidden", 0.0f, 0.6f, "tanh");
        add_node("integration_cognitive", "hidden", 0.0f, 0.6f, "tanh");
        add_node("integration_emotional", "hidden", 0.0f, 0.6f, "tanh");
        add_node("memory_short_term", "memory", 0.0f, 0.4f, "sigmoid");
        add_node("memory_long_term", "memory", 0.0f, 0.4f, "sigmoid");
        add_node("decision_rational", "decision", 0.0f, 0.7f, "sigmoid");
        add_node("decision_emotional", "decision", 0.0f, 0.7f, "sigmoid");
        add_node("decision_intuitive", "decision", 0.0f, 0.7f, "sigmoid");
        add_node("output_response", "output", 0.0f, 0.5f, "softmax");
        add_node("output_action", "output", 0.0f, 0.5f, "softmax");
        add_node("output_expression", "output", 0.0f, 0.5f, "softmax");

        connect_nodes("input_text", "processing_text", 0.8f);
        connect_nodes("processing_text", "integration_sensory", 0.7f);
        connect_nodes("integration_sensory", "integration_cognitive", 0.6f);
        connect_nodes("integration_cognitive", "decision_rational", 0.8f);
        connect_nodes("decision_rational", "output_response", 0.9f);

        connect_nodes("input_emotional", "processing_emotional", 0.8f);
        connect_nodes("processing_emotional", "integration_emotional", 0.7f);
        connect_nodes("integration_emotional", "decision_emotional", 0.8f);
        connect_nodes("decision_emotional", "output_response", 0.7f);

        connect_nodes("input_visual", "processing_visual", 0.7f);
        connect_nodes("processing_visual", "integration_sensory", 0.6f);

        connect_nodes("input_audio", "processing_audio", 0.7f);
        connect_nodes("processing_audio", "integration_sensory", 0.6f);

        connect_nodes("integration_sensory", "memory_short_term", 0.5f);
        connect_nodes("memory_short_term", "memory_long_term", 0.3f);
        connect_nodes("memory_long_term", "integration_cognitive", 0.4f);

        connect_nodes("integration_emotional", "decision_intuitive", 0.6f);
        connect_nodes("decision_intuitive", "output_response", 0.6f);
    }

    void add_node(const std::string& id, const std::string& type,
                  float activation, float threshold, const std::string& function) {
        NeuralNode node;
        node.id = id;
        node.type = type;
        node.activation = activation;
        node.threshold = threshold;
        node.function = function;
        node.bias = 0.1f;
        node.learning_rate = 0.01f;
        neural_network[id] = node;
    }

    void connect_nodes(const std::string& from, const std::string& to, float weight) {
        if (neural_network.find(from) != neural_network.end() &&
            neural_network.find(to) != neural_network.end()) {
            neural_network[to].weights[from] = weight;
            neural_network[to].connected_to.push_back(from);
        }
    }

    void initialize_cognitive_patterns() {
        cognitive_patterns["problem_solving"] = {
            "problem_solving",
            {"problem", "difficulty", "challenge", "issue", "obstacle"},
            {"analyze the situation", "identify solutions", "evaluate options", "implement action"},
            0.9f,
            {{"complexity", 0.6f}, {"urgency", 0.5f}},
            {{"frustration", 0.3f}, {"determination", 0.7f}},
            0, 0.8f
        };
        cognitive_patterns["learning"] = {
            "learning",
            {"learn", "understand", "comprehend", "study", "practice"},
            {"acquire knowledge", "practice application", "reflect on learning", "teach others"},
            0.85f,
            {{"novelty", 0.7f}, {"difficulty", 0.4f}},
            {{"curiosity", 0.8f}, {"confusion", 0.3f}},
            0, 0.7f
        };
        cognitive_patterns["creativity"] = {
            "creativity",
            {"create", "imagine", "innovate", "design", "express"},
            {"brainstorm ideas", "combine concepts", "explore possibilities", "prototype solutions"},
            0.8f,
            {{"openness", 0.7f}, {"flexibility", 0.6f}},
            {{"excitement", 0.7f}, {"curiosity", 0.8f}},
            0, 0.6f
        };
        cognitive_patterns["emotional_processing"] = {
            "emotional_processing",
            {"feel", "emotion", "heart", "soul", "spirit"},
            {"acknowledge feelings", "process emotions", "express self", "find peace"},
            0.75f,
            {{"intensity", 0.8f}, {"duration", 0.5f}},
            {{"happiness", 0.5f}, {"sadness", 0.5f}, {"anger", 0.4f}},
            0, 0.9f
        };
        cognitive_patterns["social_interaction"] = {
            "social_interaction",
            {"social", "interaction", "connection", "relationship", "community"},
            {"build rapport", "understand others", "communicate clearly", "collaborate"},
            0.7f,
            {{"connection", 0.7f}, {"communication", 0.6f}},
            {{"trust", 0.7f}, {"loneliness", 0.3f}},
            0, 0.7f
        };
    }

    void initialize_decision_frameworks() {
        DecisionFramework rational;
        rational.name = "rational";
        rational.criteria = {{"logic", 0.9f}, {"data", 0.8f}, {"analysis", 0.7f}};
        rational.weights = {{"logic", 0.4f}, {"data", 0.3f}, {"analysis", 0.3f}};
        rational.actions = {
            {"analyze", "Perform thorough analysis"},
            {"evaluate", "Evaluate all options"},
            {"decide", "Make data-driven decision"}
        };
        rational.evaluators["logic"] = [](const std::map<std::string, float>& inputs) {
            auto it = inputs.find("logic");
            return it != inputs.end() ? it->second : 0.5f;
        };
        decision_frameworks["rational"] = rational;

        DecisionFramework emotional;
        emotional.name = "emotional";
        emotional.criteria = {{"feeling", 0.9f}, {"intuition", 0.8f}, {"compassion", 0.7f}};
        emotional.weights = {{"feeling", 0.4f}, {"intuition", 0.3f}, {"compassion", 0.3f}};
        emotional.actions = {
            {"sense", "Tune into feelings"},
            {"empathize", "Connect with others"},
            {"choose", "Follow heart"}
        };
        emotional.evaluators["feeling"] = [](const std::map<std::string, float>& inputs) {
            auto it = inputs.find("feeling");
            return it != inputs.end() ? it->second : 0.5f;
        };
        decision_frameworks["emotional"] = emotional;

        DecisionFramework intuitive;
        intuitive.name = "intuitive";
        intuitive.criteria = {{"insight", 0.9f}, {"pattern", 0.8f}, {"experience", 0.7f}};
        intuitive.weights = {{"insight", 0.4f}, {"pattern", 0.3f}, {"experience", 0.3f}};
        intuitive.actions = {
            {"sense", "Trust intuition"},
            {"recognize", "See the pattern"},
            {"act", "Take inspired action"}
        };
        intuitive.evaluators["insight"] = [](const std::map<std::string, float>& inputs) {
            auto it = inputs.find("insight");
            return it != inputs.end() ? it->second : 0.5f;
        };
        decision_frameworks["intuitive"] = intuitive;
    }

    void process_input(const std::string& input) {
        iteration_count++;
        cognitive_load = 0.5f + 0.2f * std::sin(iteration_count * 0.01f);

        update_neural_network(input);
        recognize_patterns(input);
        make_decisions();
        update_memory(input);
        update_attention(input);
    }

    void update_neural_network(const std::string& input) {
        propagate_input("input_text", (float)input.length() / 100.0f);

        for (auto& [id, node] : neural_network) {
            if (node.type == "hidden" || node.type == "decision") {
                float error = node.threshold - node.activation;
                node.weights = update_weights(node.weights, error);
                node.bias += node.learning_rate * error;
            }
        }
    }

    std::map<std::string, float> update_weights(const std::map<std::string, float>& weights, float error) {
        std::map<std::string, float> updated = weights;
        float learning_rate = 0.01f;

        for (auto& [key, value] : updated) {
            value += learning_rate * error * value;
            value = std::max(0.0f, std::min(1.0f, value));
        }

        return updated;
    }

    void propagate_input(const std::string& input_node, float value) {
        if (neural_network.find(input_node) != neural_network.end()) {
            neural_network[input_node].activation = value;

            for (auto& [id, node] : neural_network) {
                auto it = node.weights.find(input_node);
                if (it != node.weights.end()) {
                    float weighted_input = value * it->second + node.bias;
                    node.activation = activate(weighted_input, node.function);
                }
            }
        }
    }

    float activate(float value, const std::string& function) {
        if (function == "relu") {
            return std::max(0.0f, value);
        } else if (function == "tanh") {
            return (float)std::tanh(value);
        } else if (function == "sigmoid") {
            return 1.0f / (1.0f + std::exp(-value));
        } else if (function == "softmax") {
            return value / (1.0f + std::abs(value));
        }
        return value;
    }

    void recognize_patterns(const std::string& input) {
        for (auto& [name, pattern] : cognitive_patterns) {
            float match_score = 0.0f;
            float total_weight = 0.0f;

            for (const auto& trigger : pattern.triggers) {
                if (input.find(trigger) != std::string::npos) {
                    match_score += 1.0f;
                    total_weight += 1.0f;
                }
            }

            if (total_weight > 0.0f) {
                pattern.frequency++;
                pattern.strength = 0.7f * pattern.strength + 0.3f * (match_score / total_weight);
                pattern.strength = std::min(1.0f, pattern.strength);
            }
        }
    }

    void make_decisions() {
        for (const auto& [name, framework] : decision_frameworks) {
            float total_score = 0.0f;
            float total_weight = 0.0f;

            for (const auto& [criterion, value] : framework.criteria) {
                float score = 0.0f;
                auto eval_it = framework.evaluators.find(criterion);
                if (eval_it != framework.evaluators.end()) {
                    score = eval_it->second({{criterion, value}});
                }
                auto weight_it = framework.weights.find(criterion);
                if (weight_it != framework.weights.end()) {
                    total_score += score * weight_it->second;
                    total_weight += weight_it->second;
                }
            }

            if (total_weight > 0.0f) {
                float decision_score = total_score / total_weight;
                if (decision_score > 0.6f && !framework.actions.empty()) {
                    recent_actions.push_back(framework.actions.begin()->second);
                    if (recent_actions.size() > 10) {
                        recent_actions.erase(recent_actions.begin());
                    }
                }
            }
        }
    }

    void update_memory(const std::string& input) {
        working_memory["last_input"] = (float)input.length() / 100.0f;
        working_memory["last_time"] = (float)std::chrono::system_clock::now().time_since_epoch().count();

        for (const auto& [name, pattern] : cognitive_patterns) {
            working_memory["pattern_" + name] = pattern.strength;
        }

        for (const auto& [name, pattern] : cognitive_patterns) {
            belief_state["belief_" + name] = pattern.strength * 0.5f + 0.3f;
        }
    }

    void update_attention(const std::string& input) {
        for (auto& [key, value] : attention_focus) {
            value = 0.0f;
        }

        for (const auto& [name, pattern] : cognitive_patterns) {
            for (const auto& trigger : pattern.triggers) {
                if (input.find(trigger) != std::string::npos) {
                    attention_focus[name] = 1.0f;
                    break;
                }
            }
        }
    }

    std::string generate_response() {
        std::string response;
        std::string best_pattern;
        float best_priority = 0.0f;

        for (const auto& [name, pattern] : cognitive_patterns) {
            if (pattern.strength > 0.5f && pattern.priority > best_priority) {
                best_priority = pattern.priority;
                best_pattern = name;
            }
        }

        if (!best_pattern.empty()) {
            auto& pattern = cognitive_patterns[best_pattern];
            if (!pattern.responses.empty()) {
                std::uniform_int_distribution<int> dist(0, (int)pattern.responses.size() - 1);
                response = pattern.responses[dist(rng)];
            }
        }

        if (response.empty()) {
            float activation = neural_network["output_response"].activation;
            if (activation > 0.6f) {
                response = "I understand what you're saying.";
            } else if (activation > 0.3f) {
                response = "Tell me more about that.";
            } else {
                response = "I'm thinking... Could you elaborate?";
            }
        }

        return response;
    }

private:
    std::mt19937 rng{std::random_device{}()};
};

// ============================================================
// HYBRID INTEGRATION ENGINE
// ============================================================

class HybridEngine {
private:
    RoslynParser roslyn;
    RegexEngine regex;
    ElizaEngine eliza;
    Adam12Engine adam12;
    std::map<std::string, std::function<void(const std::string&)>> analysis_pipeline;
    std::vector<std::string> processing_history;

public:
    HybridEngine() {
        initialize_pipeline();
    }

    void initialize_pipeline() {
        analysis_pipeline["roslyn"] = [this](const std::string& input) {
            if (input.find("class") != std::string::npos ||
                input.find("method") != std::string::npos ||
                input.find("namespace") != std::string::npos) {
                auto ast = roslyn.parse_code(input);
                auto analysis = roslyn.analyze_code(input);
                processing_history.push_back("Roslyn: " + std::to_string(ast->children.size()) + " nodes");
            }
        };

        analysis_pipeline["regex"] = [this](const std::string& input) {
            auto matches = regex.match_all(input);
            for (const auto& [pattern, values] : matches) {
                if (!values.empty()) {
                    processing_history.push_back("RegEx: " + pattern + " -> " + values[0]);
                }
            }
        };

        analysis_pipeline["eliza"] = [this](const std::string& input) {
            auto response = eliza.process_input(input);
            processing_history.push_back("ELIZA: " + response.text + " (confidence: " +
                                       std::to_string(response.confidence) + ")");
        };

        analysis_pipeline["adam12"] = [this](const std::string& input) {
            adam12.process_input(input);
            auto response = adam12.generate_response();
            processing_history.push_back("ADAM12: " + response);
        };
    }

    std::map<std::string, std::string> process_input(const std::string& input) {
        std::map<std::string, std::string> results;
        processing_history.clear();

        bool looks_like_code = input.find("class") != std::string::npos ||
                               input.find("method") != std::string::npos ||
                               input.find("namespace") != std::string::npos ||
                               input.find("public") != std::string::npos ||
                               input.find("private") != std::string::npos;

        if (looks_like_code) {
            auto ast = roslyn.parse_code(input);
            auto analysis = roslyn.analyze_code(input);
            std::string ast_summary;
            for (const auto& [key, value] : analysis) {
                ast_summary += key + ": " + value + ", ";
            }
            if (!ast_summary.empty()) ast_summary = ast_summary.substr(0, ast_summary.size() - 2);
            results["roslyn"] = ast_summary;
        } else {
            results["roslyn"] = "";
        }

        auto matches = regex.get_all_matches(input);
        std::string regex_summary;
        for (const auto& [pattern, values] : matches) {
            regex_summary += pattern + "(" + std::to_string(values.size()) + "), ";
        }
        if (!regex_summary.empty()) regex_summary = regex_summary.substr(0, regex_summary.size() - 2);
        results["regex"] = regex_summary.empty() ? "No patterns matched" : regex_summary;

        auto eliza_response = eliza.process_input(input);
        results["eliza"] = eliza_response.text;

        adam12.process_input(input);
        auto adam_response = adam12.generate_response();
        results["adam12"] = adam_response;

        std::string hybrid_response;
        if (!results["roslyn"].empty()) {
            hybrid_response += "[Code Analysis: " + results["roslyn"] + "] ";
        }
        if (regex.matches(input, "emotion") || regex.matches(input, "need") || regex.matches(input, "request")) {
            hybrid_response += "[ELIZA: " + results["eliza"] + "] ";
        }
        if (regex.matches(input, "question") || regex.matches(input, "request") || looks_like_code) {
            hybrid_response += "[ADAM12: " + results["adam12"] + "] ";
        }
        if (hybrid_response.empty()) {
            hybrid_response = results["eliza"];
        }

        results["hybrid"] = hybrid_response;
        results["history"] = std::to_string(processing_history.size()) + " steps processed";

        return results;
    }

    std::map<std::string, std::string> analyze_code(const std::string& code) {
        return roslyn.analyze_code(code);
    }

    json extract_relations(const std::string& code) {
        return roslyn.extract_relations(code);
    }

    std::map<std::string, std::vector<std::string>> extract_patterns(const std::string& text) {
        return regex.get_all_matches(text);
    }

    std::string get_therapeutic_response(const std::string& input) {
        auto response = eliza.process_input(input);
        return response.text;
    }

    std::string get_cognitive_response(const std::string& input) {
        adam12.process_input(input);
        return adam12.generate_response();
    }
};

// ============================================================
// MAIN RUNTIME
// ============================================================

class QuantumTrinityHybridRuntime {
private:
    HybridEngine hybrid;
    std::map<std::string, std::vector<std::string>> session_history;
    std::map<std::string, std::map<std::string, std::string>> user_contexts;

public:
    QuantumTrinityHybridRuntime() {
        if (!g_quiet) {
            std::cout << "🌌 Quantum Trinity Hybrid Runtime v9.0" << std::endl;
            std::cout << "============================================" << std::endl;
            std::cout << "✅ Roslyn Parser: Initialized" << std::endl;
            std::cout << "✅ Regex Engine: Active (30+ patterns)" << std::endl;
            std::cout << "✅ ELIZA Engine: Therapeutic mode ready" << std::endl;
            std::cout << "✅ ADAM12 Engine: Cognitive processing active" << std::endl;
            std::cout << "✅ Hybrid Integration: Engaged" << std::endl;
            std::cout << std::endl;
        }
    }

    json process_request(const json& request) {
        json response;
        std::string operation = request.value("operation", std::string("process"));
        std::string session_id = request.value("session_id", std::string("default"));

        if (operation == "process") {
            std::string input = request.value("input", std::string(""));
            std::string mode = request.value("mode", std::string("hybrid"));

            if (input.empty()) {
                response["status"] = "error";
                response["message"] = "Empty input provided";
                return response;
            }

            auto results = hybrid.process_input(input);
            session_history[session_id].push_back(input);
            session_history[session_id].push_back(results["hybrid"]);

            response["status"] = "success";
            response["operation"] = operation;
            response["input"] = input;

            if (mode == "hybrid") {
                response["output"] = results["hybrid"];
                response["mode"] = "hybrid";
            } else if (mode == "eliza") {
                response["output"] = results["eliza"];
                response["mode"] = "eliza";
            } else if (mode == "adam12") {
                response["output"] = results["adam12"];
                response["mode"] = "adam12";
            } else if (mode == "code") {
                response["output"] = results["roslyn"];
                response["mode"] = "code";
            } else if (mode == "pattern") {
                response["output"] = results["regex"];
                response["mode"] = "pattern";
            } else {
                response["output"] = results["hybrid"];
                response["mode"] = "default";
            }

            response["analysis"] = json::object();
            for (const auto& [key, value] : results) {
                if (key != "hybrid" && key != "history") {
                    response["analysis"][key] = value;
                }
            }

        } else if (operation == "analyze_code") {
            std::string code = request.value("code", std::string(""));
            if (code.empty()) {
                response["status"] = "error";
                response["message"] = "Empty code provided";
                return response;
            }

            auto analysis = hybrid.analyze_code(code);
            response["status"] = "success";
            response["operation"] = operation;
            response["code_length"] = (int)code.length();
            response["analysis"] = json(analysis);

        } else if (operation == "extract_relations") {
            std::string code = request.value("code", std::string(""));
            if (code.empty()) code = request.value("text", std::string(""));
            if (code.empty()) {
                response["status"] = "error";
                response["message"] = "Empty code provided";
                return response;
            }
            response["status"] = "success";
            response["operation"] = operation;
            response["edges"] = hybrid.extract_relations(code);

        } else if (operation == "extract_patterns") {
            std::string text = request.value("text", std::string(""));
            auto patterns = hybrid.extract_patterns(text);
            response["status"] = "success";
            response["operation"] = operation;
            response["patterns"] = json::object();
            for (const auto& [pattern, matches] : patterns) {
                response["patterns"][pattern] = matches;
            }

        } else if (operation == "get_history") {
            response["status"] = "success";
            response["operation"] = operation;
            response["session_id"] = session_id;
            response["history"] = session_history[session_id];

        } else {
            response["status"] = "error";
            response["message"] = "Unknown operation: " + operation;
        }

        return response;
    }
};

// ============================================================
// CLI / JSON-RPC SERVER
// ============================================================

json read_request_from_args(int argc, char** argv) {
    json request;
    if (argc > 1) {
        std::string arg = argv[1];
        if (arg == "process" && argc > 2) {
            request["operation"] = "process";
            request["input"] = argv[2];
            request["session_id"] = (argc > 3) ? argv[3] : "cli_session";
            request["mode"] = (argc > 4) ? argv[4] : "hybrid";
            return request;
        }
        try {
            request = json::parse(arg);
            return request;
        } catch (...) {
            request["operation"] = "process";
            request["input"] = arg;
            request["session_id"] = "cli_session";
            request["mode"] = "hybrid";
            return request;
        }
    }
    return request;
}

json read_request_from_stdin() {
    json request;
    std::string line;
    std::string input;
    while (std::getline(std::cin, line)) {
        input += line + "\n";
    }
    if (!input.empty()) {
        try {
            request = json::parse(input);
        } catch (...) {
            request["operation"] = "process";
            request["input"] = input;
            request["session_id"] = "stdin_session";
            request["mode"] = "hybrid";
        }
    }
    return request;
}

int main(int argc, char** argv) {
    g_quiet = !is_interactive();
    int arg_idx = 1;
    while (arg_idx < argc) {
        std::string a = argv[arg_idx];
        if (a == "--quiet" || a == "-q") {
            g_quiet = true;
            for (int j = arg_idx; j + 1 < argc; j++) argv[j] = argv[j + 1];
            argc--;
        } else {
            arg_idx++;
        }
    }

    QuantumTrinityHybridRuntime runtime;

    json request;
    if (argc > 1) {
        request = read_request_from_args(argc, argv);
    } else {
        if (!std::cin.eof() && std::cin.peek() != EOF) {
            request = read_request_from_stdin();
        }
    }

    if (request.is_null() || request.empty()) {
        request["operation"] = "process";
        request["session_id"] = "demo_session";
        request["input"] = "I'm feeling frustrated about this coding problem. The class structure is confusing me.";
        request["mode"] = "hybrid";
        g_quiet = false;
    }

    auto response = runtime.process_request(request);
    std::cout << response.dump(2) << std::endl;
    return 0;
}

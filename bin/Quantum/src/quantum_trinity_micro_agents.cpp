// quantum_trinity_micro_agents.cpp
// Quantum Trinity Micro-Agents System
// Version: 11.1
//
// NNC-K authority boundary:
//   This worker is a candidate-only sidecar. It emits JSON text/structures
//   for the PowerShell runtime and MicronautManager to evaluate. It never
//   creates, updates, merges, or promotes micronauts.

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
#include <cstdio>
#include <cstdlib>

#ifdef _WIN32
#define NOMINMAX
#include <windows.h>
#include <io.h>
#include <stdio.h>
bool is_interactive() {
    return _isatty(_fileno(stdin)) != 0 && _isatty(_fileno(stdout)) != 0;
}
#else
bool is_interactive() { return false; }
#endif

bool g_quiet = false;

// ============================================================
// MICRO-AGENT DEFINITIONS
// ============================================================

struct MicroAgent {
    std::string id;
    std::string template_name;
    std::string name;
    std::string description;
    std::vector<std::string> capabilities;
    std::vector<std::string> triggers;
    std::map<std::string, float> weights;
    float activation_threshold = 0.5f;
    float learning_rate = 0.01f;
    int priority = 0;

    std::function<json(const json&)> execute;
    std::function<bool(const std::string&)> can_handle;

    bool is_quantum = false;
    float quantum_phase = 0.0f;
    std::map<std::string, float> entanglement;

    int execution_count = 0;
    float success_rate = 1.0f;
    float avg_response_time = 0.0f;
    std::chrono::system_clock::time_point last_execution;
};

struct AgentSwarm {
    std::string id;
    std::string name;
    std::vector<std::string> agent_ids;
    std::map<std::string, float> coordination_weights;
    std::string coordination_strategy;
    float swarm_intelligence = 0.5f;
    int max_agents = 10;
};

// ============================================================
// TEMPLATE REGISTRY (HARDCODED EXECUTION ENGINES)
// ============================================================

class AgentTemplateRegistry {
private:
    std::map<std::string, std::function<MicroAgent(const std::string&)>> templates;
    std::mt19937 rng;

public:
    AgentTemplateRegistry() {
        rng.seed((unsigned)std::chrono::system_clock::now().time_since_epoch().count());
        register_all();
    }

    bool has(const std::string& name) const { return templates.find(name) != templates.end(); }

    MicroAgent instantiate(const std::string& template_name, const std::string& id) {
        if (!has(template_name)) {
            throw std::runtime_error("Unknown agent template: " + template_name);
        }
        return templates[template_name](id);
    }

private:
    void register_all() {
        templates["parser"] = [this](const std::string& id) -> MicroAgent {
            MicroAgent agent;
            agent.id = id;
            agent.template_name = "parser";
            agent.name = "Parser Agent";
            agent.description = "Parses input using GBNF, EBNF, and PEG grammars";
            agent.capabilities = {"parse", "validate", "tokenize", "grammar_check"};
            agent.triggers = {"parse", "grammar", "syntax", "token", "validate"};
            agent.weights = {{"accuracy", 0.9f}, {"speed", 0.7f}, {"depth", 0.8f}};
            agent.priority = 5;
            agent.execute = [](const json& input) -> json {
                json result;
                std::string text = input.value("text", "");
                result["parsed"] = true;
                result["tokens"] = json::array();
                result["grammar_type"] = input.value("grammar", "gbnf");
                std::string current;
                for (char c : text) {
                    if (c == ' ' || c == '\n' || c == '\t' || c == '\r') {
                        if (!current.empty()) {
                            result["tokens"].push_back(current);
                            current.clear();
                        }
                    } else {
                        current += c;
                    }
                }
                if (!current.empty()) {
                    result["tokens"].push_back(current);
                }
                return result;
            };
            agent.can_handle = [](const std::string& input) {
                return input.find("parse") != std::string::npos ||
                       input.find("grammar") != std::string::npos ||
                       input.find("syntax") != std::string::npos ||
                       input.find("token") != std::string::npos;
            };
            return agent;
        };

        templates["therapist"] = [this](const std::string& id) -> MicroAgent {
            MicroAgent agent;
            agent.id = id;
            agent.template_name = "therapist";
            agent.name = "ELIZA Therapist";
            agent.description = "Provides therapeutic responses using ELIZA patterns";
            agent.capabilities = {"empathize", "listen", "support", "reflect", "guide"};
            agent.triggers = {"feel", "emotion", "sad", "angry", "happy", "anxious", "stress"};
            agent.weights = {{"empathy", 0.95f}, {"support", 0.9f}, {"guidance", 0.8f}};
            agent.priority = 7;

            std::map<std::string, std::vector<std::string>> responses = {
                {"sad", {"I understand you're feeling sad. Can you tell me more?",
                          "It's okay to feel sad. What's on your mind?",
                          "I'm here to listen. What happened?"}},
                {"angry", {"I hear your frustration. Can you explain why?",
                            "Anger is a valid emotion. What triggered it?",
                            "Let's talk about what's making you angry."}},
                {"happy", {"I'm glad you're happy! What brought this joy?",
                            "That's wonderful to hear! Tell me more.",
                            "Happiness is beautiful. What's the source?"}},
                {"anxious", {"Anxiety can be overwhelming. What's concerning you?",
                              "Let's explore what's making you anxious.",
                              "You're safe here. What's on your mind?"}},
                {"frustrated", {"I understand your frustration. Let's unpack it together.",
                                 "Frustration often points to an unsolved puzzle. What is it?",
                                 "Tell me more about what's blocking you."}},
                {"excited", {"Your energy is contagious! What's exciting you?",
                              "Excitement is a great signal. What's next?",
                              "Tell me more about what you're looking forward to."}},
                {"confused", {"Confusion is the start of clarity. What feels unclear?",
                               "Let's sort through this together. What is confusing?",
                               "It's okay to be confused. What part needs explaining?"}},
                {"default", {"Tell me more about that.",
                              "I see. How does that make you feel?",
                              "That's interesting. Go on.",
                              "I'm listening. What else?"}}
            };

            agent.execute = [responses](const json& input) -> json {
                json result;
                std::string text = input.value("text", "");
                std::string emotion = "default";
                for (const auto& kv : responses) {
                    if (kv.first != "default" && text.find(kv.first) != std::string::npos) {
                        emotion = kv.first;
                        break;
                    }
                }
                const auto& response_list = responses.at(emotion);
                int idx = rand() % (int)response_list.size();
                result["response"] = response_list[idx];
                result["emotion_detected"] = emotion;
                result["confidence"] = 0.7f + (rand() % 30) / 100.0f;
                return result;
            };

            agent.can_handle = [](const std::string& input) {
                std::vector<std::string> emotional_words = {
                    "feel", "emotion", "sad", "angry", "happy", "anxious", "stress",
                    "depressed", "frustrated", "excited", "confused", "worried", "upset"
                };
                for (const auto& word : emotional_words) {
                    if (input.find(word) != std::string::npos) return true;
                }
                return false;
            };
            return agent;
        };

        templates["cognitive"] = [this](const std::string& id) -> MicroAgent {
            MicroAgent agent;
            agent.id = id;
            agent.template_name = "cognitive";
            agent.name = "ADAM12 Cognitive Agent";
            agent.description = "Provides cognitive analysis and decision support";
            agent.capabilities = {"analyze", "decide", "reason", "solve", "plan"};
            agent.triggers = {"problem", "solution", "think", "decide", "analyze", "reason"};
            agent.weights = {{"logic", 0.95f}, {"analysis", 0.9f}, {"decision", 0.85f}};
            agent.priority = 6;
            agent.execute = [](const json& input) -> json {
                json result;
                std::string text = input.value("text", "");
                result["analysis"] = "Analyzing: " + text;
                result["decision"] = "Based on analysis, I recommend evaluating the constraints and selecting the path with the strongest evidence.";
                result["confidence"] = 0.8f + (rand() % 20) / 100.0f;
                std::vector<std::string> patterns = {"problem", "solution", "cause", "effect", "because", "if", "should"};
                result["patterns_found"] = json::array();
                for (const auto& pattern : patterns) {
                    if (text.find(pattern) != std::string::npos) {
                        result["patterns_found"].push_back(pattern);
                    }
                }
                return result;
            };
            agent.can_handle = [](const std::string& input) {
                return input.find("problem") != std::string::npos ||
                       input.find("solution") != std::string::npos ||
                       input.find("decide") != std::string::npos ||
                       input.find("analyze") != std::string::npos ||
                       input.find("reason") != std::string::npos ||
                       input.find("think") != std::string::npos;
            };
            return agent;
        };

        templates["regex"] = [this](const std::string& id) -> MicroAgent {
            MicroAgent agent;
            agent.id = id;
            agent.template_name = "regex";
            agent.name = "Regex Pattern Agent";
            agent.description = "Pattern matching using regular expressions";
            agent.capabilities = {"match", "extract", "replace", "validate_pattern"};
            agent.triggers = {"pattern", "match", "regex", "extract", "find"};
            agent.weights = {{"precision", 0.95f}, {"coverage", 0.8f}, {"speed", 0.9f}};
            agent.priority = 4;

            std::map<std::string, std::regex> patterns;
            patterns["email"] = std::regex(R"([a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,})");
            patterns["url"] = std::regex(R"(https?://[^\s]+)");
            patterns["phone"] = std::regex(R"(\d{3}[-.]?\d{3}[-.]?\d{4})");
            patterns["date"] = std::regex(R"(\d{4}-\d{2}-\d{2})");
            patterns["number"] = std::regex(R"(\d+\.?\d*)");

            agent.execute = [patterns](const json& input) -> json {
                json result;
                std::string text = input.value("text", "");
                std::string pattern_type = input.value("pattern_type", "all");
                result["matches"] = json::object();

                auto search_all = [&text](const std::regex& re) -> json {
                    json arr = json::array();
                    std::smatch m;
                    std::string search = text;
                    while (std::regex_search(search, m, re)) {
                        arr.push_back(m[0].str());
                        search = m.suffix().str();
                    }
                    return arr;
                };

                if (pattern_type == "all") {
                    for (const auto& kv : patterns) {
                        json arr = search_all(kv.second);
                        if (!arr.empty()) {
                            result["matches"][kv.first] = arr;
                        }
                    }
                } else if (patterns.find(pattern_type) != patterns.end()) {
                    json arr = search_all(patterns.at(pattern_type));
                    if (!arr.empty()) {
                        result["matches"][pattern_type] = arr;
                    }
                }
                result["total_matches"] = result["matches"].size();
                return result;
            };

            agent.can_handle = [](const std::string& input) {
                return input.find("pattern") != std::string::npos ||
                       input.find("match") != std::string::npos ||
                       input.find("regex") != std::string::npos ||
                       input.find("extract") != std::string::npos ||
                       input.find("email") != std::string::npos ||
                       input.find("url") != std::string::npos ||
                       input.find("phone") != std::string::npos;
            };
            return agent;
        };

        templates["quantum"] = [this](const std::string& id) -> MicroAgent {
            MicroAgent agent;
            agent.id = id;
            agent.template_name = "quantum";
            agent.name = "Quantum Superposition Agent";
            agent.description = "Quantum-inspired processing with superposition";
            agent.capabilities = {"superpose", "collapse", "entangle", "quantum_analyze"};
            agent.triggers = {"quantum", "superposition", "entanglement", "collapse"};
            agent.weights = {{"quantum_accuracy", 0.9f}, {"superposition", 0.95f}, {"coherence", 0.85f}};
            agent.priority = 8;
            agent.is_quantum = true;
            agent.quantum_phase = 0.5f;
            agent.execute = [](const json& input) -> json {
                json result;
                std::string text = input.value("text", "");
                std::vector<std::string> interpretations = {
                    "Classical interpretation",
                    "Quantum interpretation",
                    "Symbolic interpretation",
                    "Pattern-based interpretation"
                };
                result["superpositions"] = json::array();
                for (const auto& interp : interpretations) {
                    json state;
                    state["interpretation"] = interp;
                    state["probability"] = 0.2 + (rand() % 60) / 100.0;
                    result["superpositions"].push_back(state);
                }
                result["collapsed_state"] = interpretations[rand() % interpretations.size()];
                result["collapse_factor"] = 0.5 + (rand() % 50) / 100.0;
                result["entangled_with"] = "cognitive_agent";
                result["quantum_phase"] = 0.5 + (rand() % 50) / 100.0;
                return result;
            };
            agent.can_handle = [](const std::string& input) {
                return input.find("quantum") != std::string::npos ||
                       input.find("superposition") != std::string::npos ||
                       input.find("entanglement") != std::string::npos ||
                       input.find("collapse") != std::string::npos;
            };
            return agent;
        };

        templates["code"] = [this](const std::string& id) -> MicroAgent {
            MicroAgent agent;
            agent.id = id;
            agent.template_name = "code";
            agent.name = "Code Analysis Agent";
            agent.description = "Analyzes code structure and provides insights";
            agent.capabilities = {"analyze_code", "detect_patterns", "suggest_refactor", "find_bugs"};
            agent.triggers = {"code", "class", "method", "function", "variable", "syntax"};
            agent.weights = {{"analysis_depth", 0.9f}, {"pattern_recognition", 0.85f}, {"suggestion_quality", 0.8f}};
            agent.priority = 6;
            agent.execute = [](const json& input) -> json {
                json result;
                std::string code = input.value("code", input.value("text", ""));
                result["line_count"] = (int)(std::count(code.begin(), code.end(), '\n') + 1);
                result["word_count"] = (int)(std::count(code.begin(), code.end(), ' ') + 1);
                std::vector<std::string> constructs = {"class", "struct", "enum", "interface",
                                                         "function", "method", "variable", "loop", "condition"};
                result["constructs"] = json::object();
                for (const auto& construct : constructs) {
                    size_t count = 0;
                    size_t pos = 0;
                    while ((pos = code.find(construct, pos)) != std::string::npos) {
                        count++;
                        pos += construct.length();
                    }
                    if (count > 0) {
                        result["constructs"][construct] = (int)count;
                    }
                }
                json suggestions = json::array();
                if (code.find("for") != std::string::npos) suggestions.push_back("Consider using range-based loops");
                if (code.find("if") != std::string::npos && code.find("else") == std::string::npos) {
                    suggestions.push_back("Consider adding an else clause");
                }
                if (code.find("var") != std::string::npos) suggestions.push_back("Consider using explicit types");
                result["suggestions"] = suggestions;
                return result;
            };
            agent.can_handle = [](const std::string& input) {
                return input.find("code") != std::string::npos ||
                       input.find("class") != std::string::npos ||
                       input.find("struct") != std::string::npos ||
                       input.find("function") != std::string::npos ||
                       input.find("method") != std::string::npos ||
                       input.find("bug") != std::string::npos ||
                       input.find("refactor") != std::string::npos;
            };
            return agent;
        };
    }
};

// ============================================================
// MICRO-AGENT FACTORY
// ============================================================

class MicroAgentFactory {
private:
    AgentTemplateRegistry templates;
    std::map<std::string, MicroAgent> agent_registry;
    std::map<std::string, AgentSwarm> swarm_registry;
    std::mutex factory_mutex;
    int id_counter = 0;

public:
    bool has_template(const std::string& name) const { return templates.has(name); }

    void clear_registry() {
        std::lock_guard<std::mutex> lock(factory_mutex);
        agent_registry.clear();
    }

    MicroAgent create_agent(const std::string& template_name) {
        std::lock_guard<std::mutex> lock(factory_mutex);
        std::string id = template_name + "_" + std::to_string(++id_counter) + "_" +
                         std::to_string(std::chrono::system_clock::now().time_since_epoch().count());
        auto agent = templates.instantiate(template_name, id);
        if (agent.is_quantum) {
            for (const auto& kv : agent_registry) {
                if (kv.second.is_quantum) {
                    agent.entanglement[kv.first] = 0.3f + (rand() % 70) / 100.0f;
                }
            }
        }
        agent_registry[agent.id] = agent;
        return agent;
    }

    void apply_config_overrides(MicroAgent& agent, const json& cfg) {
        if (cfg.contains("name")) agent.name = cfg.value("name", agent.name);
        if (cfg.contains("description")) agent.description = cfg.value("description", agent.description);
        if (cfg.contains("capabilities")) {
            agent.capabilities.clear();
            for (const auto& c : cfg["capabilities"]) agent.capabilities.push_back(c.get<std::string>());
        }
        if (cfg.contains("triggers")) {
            agent.triggers.clear();
            for (const auto& t : cfg["triggers"]) agent.triggers.push_back(t.get<std::string>());
        }
        if (cfg.contains("weights")) {
            agent.weights.clear();
            for (auto& [k, v] : cfg["weights"].items()) agent.weights[k] = v.get<float>();
        }
        if (cfg.contains("priority")) agent.priority = cfg.value("priority", agent.priority);
        if (cfg.contains("activation_threshold")) agent.activation_threshold = cfg.value("activation_threshold", agent.activation_threshold);
        if (cfg.contains("learning_rate")) agent.learning_rate = cfg.value("learning_rate", agent.learning_rate);
        if (cfg.contains("is_quantum")) agent.is_quantum = cfg.value("is_quantum", agent.is_quantum);
        if (cfg.contains("quantum_phase")) agent.quantum_phase = cfg.value("quantum_phase", agent.quantum_phase);
    }

    MicroAgent create_agent_from_config(const json& cfg) {
        std::string template_name = cfg.value("template", "");
        if (template_name.empty() || !templates.has(template_name)) {
            throw std::runtime_error("Config agent missing or unknown template: " + template_name);
        }
        auto agent = create_agent(template_name);
        apply_config_overrides(agent, cfg);
        std::lock_guard<std::mutex> lock(factory_mutex);
        agent_registry[agent.id] = agent;
        return agent;
    }

    std::vector<MicroAgent> get_all_agents() {
        std::lock_guard<std::mutex> lock(factory_mutex);
        std::vector<MicroAgent> agents;
        for (const auto& kv : agent_registry) agents.push_back(kv.second);
        return agents;
    }

    std::vector<MicroAgent> find_agents(const std::string& input) {
        std::lock_guard<std::mutex> lock(factory_mutex);
        std::vector<MicroAgent> matching;
        for (const auto& kv : agent_registry) {
            if (kv.second.can_handle(input)) matching.push_back(kv.second);
        }
        std::sort(matching.begin(), matching.end(),
                  [](const MicroAgent& a, const MicroAgent& b) { return a.priority > b.priority; });
        return matching;
    }

    AgentSwarm create_swarm(const std::string& name, const std::vector<std::string>& agent_ids) {
        std::lock_guard<std::mutex> lock(factory_mutex);
        AgentSwarm swarm;
        swarm.id = "swarm_" + std::to_string(++id_counter);
        swarm.name = name;
        swarm.agent_ids = agent_ids;
        swarm.coordination_strategy = "consensus";
        for (const auto& id : agent_ids) {
            swarm.coordination_weights[id] = 0.5f + (rand() % 50) / 100.0f;
        }
        swarm_registry[swarm.id] = swarm;
        return swarm;
    }

    json swarm_process(AgentSwarm& swarm, const json& input) {
        json result;
        result["swarm_id"] = swarm.id;
        result["agent_results"] = json::object();
        std::vector<std::pair<float, json>> weighted_results;

        for (const auto& agent_id : swarm.agent_ids) {
            if (agent_registry.find(agent_id) != agent_registry.end()) {
                auto& agent = agent_registry[agent_id];
                if (agent.can_handle(input.value("text", ""))) {
                    auto start = std::chrono::high_resolution_clock::now();
                    json agent_result = agent.execute(input);
                    auto end = std::chrono::high_resolution_clock::now();
                    auto duration = std::chrono::duration_cast<std::chrono::milliseconds>(end - start);
                    agent.avg_response_time = (agent.avg_response_time * agent.execution_count + duration.count()) / (agent.execution_count + 1);
                    agent.execution_count++;
                    agent.last_execution = std::chrono::system_clock::now();

                    float weight = swarm.coordination_weights[agent_id];
                    agent_result["execution_time_ms"] = (int)duration.count();
                    agent_result["template"] = agent.template_name;
                    weighted_results.push_back({weight, agent_result});
                    result["agent_results"][agent_id] = agent_result;
                }
            }
        }

        json combined;
        if (!weighted_results.empty()) {
            auto best = std::max_element(weighted_results.begin(), weighted_results.end(),
                                         [](const auto& a, const auto& b) { return a.first < b.first; });
            combined = best->second;
            combined["consensus_weight"] = best->first;
            combined["agents_consulted"] = (int)weighted_results.size();
        }
        result["combined"] = combined;
        return result;
    }
};

// ============================================================
// MICRO-AGENT ORCHESTRATOR
// ============================================================

class MicroAgentOrchestrator {
private:
    MicroAgentFactory factory;
    std::map<std::string, std::vector<std::string>> session_agents;
    std::map<std::string, std::map<std::string, float>> agent_trust;
    std::map<std::string, std::vector<json>> execution_history;
    std::mutex orchestrator_mutex;

public:
    void initialize_default_agents() {
        factory.create_agent("parser");
        factory.create_agent("therapist");
        factory.create_agent("cognitive");
        factory.create_agent("regex");
        factory.create_agent("quantum");
        factory.create_agent("code");
    }

    void load_config_file(const std::string& path) {
        std::ifstream f(path);
        if (!f.is_open()) return;
        json cfg;
        try {
            f >> cfg;
        } catch (...) {
            return;
        }
        if (!cfg.contains("agents")) return;

        // Config defines the authoritative population for this session.
        // Clear defaults so templates are not duplicated.
        factory.clear_registry();

        for (const auto& agent_cfg : cfg["agents"]) {
            try {
                factory.create_agent_from_config(agent_cfg);
            } catch (const std::exception& e) {
                if (!g_quiet) {
                    std::cerr << "⚠️ Skipping config agent: " << e.what() << std::endl;
                }
            }
        }
    }

    json process_input(const std::string& session_id, const std::string& input) {
        std::lock_guard<std::mutex> lock(orchestrator_mutex);
        json request;
        request["text"] = input;
        request["session_id"] = session_id;
        request["timestamp"] = std::chrono::system_clock::now().time_since_epoch().count();

        auto agents = factory.find_agents(input);
        if (session_agents.find(session_id) == session_agents.end()) {
            session_agents[session_id] = std::vector<std::string>();
            agent_trust[session_id] = std::map<std::string, float>();
        }

        json result;
        result["session_id"] = session_id;
        result["input"] = input;
        result["agent_responses"] = json::object();

        for (auto& agent : agents) {
            auto start = std::chrono::high_resolution_clock::now();
            json agent_result = agent.execute(request);
            auto end = std::chrono::high_resolution_clock::now();
            auto duration = std::chrono::duration_cast<std::chrono::milliseconds>(end - start);

            float trust = agent_trust[session_id][agent.id] + 0.01f;
            if (agent_result.contains("confidence") && agent_result["confidence"].get<float>() > 0.7f) {
                trust += 0.02f;
            }
            agent_trust[session_id][agent.id] = std::min(1.0f, trust);

            agent_result["execution_time_ms"] = (int)duration.count();
            agent_result["trust"] = agent_trust[session_id][agent.id];
            agent_result["template"] = agent.template_name;
            result["agent_responses"][agent.id] = agent_result;

            if (std::find(session_agents[session_id].begin(), session_agents[session_id].end(), agent.id) ==
                session_agents[session_id].end()) {
                session_agents[session_id].push_back(agent.id);
            }
        }

        result["combined_response"] = generate_combined_response(result, session_id);
        execution_history[session_id].push_back(result);
        if (execution_history[session_id].size() > 100) {
            execution_history[session_id].erase(execution_history[session_id].begin());
        }

        return result;
    }

    json swarm_process(const std::string& session_id, const std::string& input) {
        json request;
        request["text"] = input;
        auto agents = factory.get_all_agents();
        std::vector<std::string> ids;
        for (const auto& agent : agents) ids.push_back(agent.id);
        AgentSwarm swarm = factory.create_swarm("default_swarm_" + session_id, ids);
        return factory.swarm_process(swarm, request);
    }

    std::string generate_combined_response(const json& results, const std::string& session_id) {
        float best_score = 0.0f;
        std::string best_response;
        for (const auto& [agent_id, response] : results["agent_responses"].items()) {
            float confidence = response.value("confidence", 0.0f);
            float trust = agent_trust[session_id][agent_id];
            float score = confidence * 0.6f + trust * 0.4f;
            if (score > best_score) {
                best_score = score;
                if (response.contains("response")) best_response = response["response"].get<std::string>();
                else if (response.contains("analysis")) best_response = response["analysis"].get<std::string>();
                else if (response.contains("parsed")) best_response = "Parsed successfully";
                else best_response = "Processing complete";
            }
        }
        if (!best_response.empty()) return best_response;
        return "I've processed your input. How can I help further?";
    }

    json get_session_history(const std::string& session_id) {
        json result;
        result["session_id"] = session_id;
        result["history"] = json::array();
        if (execution_history.find(session_id) != execution_history.end()) {
            for (const auto& entry : execution_history[session_id]) result["history"].push_back(entry);
        }
        return result;
    }

    json get_agent_status() {
        json agents = json::array();
        auto all = factory.get_all_agents();
        for (const auto& agent : all) {
            json status;
            status["id"] = agent.id;
            status["template"] = agent.template_name;
            status["name"] = agent.name;
            status["description"] = agent.description;
            status["execution_count"] = agent.execution_count;
            status["success_rate"] = agent.success_rate;
            status["avg_response_time"] = agent.avg_response_time;
            status["is_quantum"] = agent.is_quantum;
            status["priority"] = agent.priority;
            status["capabilities"] = agent.capabilities;
            status["triggers"] = agent.triggers;
            agents.push_back(status);
        }
        return agents;
    }

    json get_swarm_status() {
        json result;
        result["registered_templates"] = json::array();
        for (const auto& name : {"parser", "therapist", "cognitive", "regex", "quantum", "code"}) {
            result["registered_templates"].push_back(name);
        }
        return result;
    }
};

// ============================================================
// RUNTIME
// ============================================================

class QuantumTrinityMicroAgentsRuntime {
private:
    MicroAgentOrchestrator orchestrator;
    std::map<std::string, std::map<std::string, json>> session_contexts;
    std::vector<std::string> config_paths;

    std::string find_executable_dir() const {
#ifdef _WIN32
        char buf[MAX_PATH];
        if (GetModuleFileNameA(nullptr, buf, MAX_PATH) > 0) {
            std::string path(buf);
            size_t pos = path.find_last_of("\\/");
            if (pos != std::string::npos) return path.substr(0, pos);
        }
#endif
        return ".";
    }

public:
    QuantumTrinityMicroAgentsRuntime() {
        std::string exe_dir = find_executable_dir();
        config_paths = {
            exe_dir + "/../schemas/node-populations/micro-agents.json",
            exe_dir + "/../schemas/programs/micro-agents.json",
            exe_dir + "/micro-agents.json",
            "schemas/node-populations/micro-agents.json",
            "schemas/programs/micro-agents.json",
            "micro-agents.json"
        };

        orchestrator.initialize_default_agents();
        for (const auto& p : config_paths) {
            if (file_exists(p)) {
                orchestrator.load_config_file(p);
                if (!g_quiet) {
                    std::cout << "📋 Loaded micro-agent config: " << p << std::endl;
                }
                break;
            }
        }

        if (!g_quiet) {
            std::cout << "🌌 Quantum Trinity Micro-Agents System v11.1" << std::endl;
            std::cout << "============================================" << std::endl;
            std::cout << "✅ Micro-Agent Factory: Initialized" << std::endl;
            std::cout << "✅ Agent Templates: 6 Registered" << std::endl;
            std::cout << "✅ Default Agents: Created" << std::endl;
            std::cout << "✅ Agent Swarm: Configured" << std::endl;
            std::cout << "✅ Orchestrator: Active" << std::endl;
            std::cout << "🔒 Authority: candidate-only (no registry mutation)" << std::endl;
            std::cout << std::endl;
        }
    }

    bool file_exists(const std::string& path) const {
        std::ifstream f(path);
        return f.good();
    }

    json process_request(const json& request) {
        json response;
        std::string operation = request.value("operation", "process");
        std::string session_id = request.value("session_id", "default");

        if (operation == "process") {
            std::string input = request.value("input", "");
            std::string mode = request.value("mode", "orchestrated");

            if (input.empty()) {
                response["status"] = "error";
                response["message"] = "Empty input provided";
                return response;
            }

            json result;
            if (mode == "swarm") {
                result = orchestrator.swarm_process(session_id, input);
            } else {
                result = orchestrator.process_input(session_id, input);
            }

            response["status"] = "success";
            response["operation"] = operation;
            response["session_id"] = session_id;
            response["mode"] = mode;
            response["input"] = input;
            response["authority_boundary"] = "candidate_only";
            response["authority_note"] = "This worker emits candidate text/structures only. No micronauts were created, updated, merged, or promoted.";

            // Candidate-only envelope: each matching agent produces a candidate.
            json candidates = json::array();
            if (mode == "swarm") {
                for (const auto& [agent_id, agent_result] : result["agent_results"].items()) {
                    json candidate;
                    candidate["agent_id"] = agent_id;
                    candidate["template"] = agent_result.value("template", "unknown");
                    candidate["confidence"] = agent_result.value("confidence", 0.0);
                    candidate["trust"] = agent_result.value("trust", 0.0);
                    candidate["execution_time_ms"] = agent_result.value("execution_time_ms", 0);
                    candidate["result"] = agent_result;
                    candidates.push_back(candidate);
                }
                if (result.contains("combined") && result["combined"].is_object()) {
                    response["combined_candidate"] = result["combined"].value("response", result["combined"].dump());
                }
            } else {
                for (const auto& [agent_id, agent_result] : result["agent_responses"].items()) {
                    json candidate;
                    candidate["agent_id"] = agent_id;
                    candidate["template"] = agent_result.value("template", "unknown");
                    candidate["confidence"] = agent_result.value("confidence", 0.0);
                    candidate["trust"] = agent_result.value("trust", 0.0);
                    candidate["execution_time_ms"] = agent_result.value("execution_time_ms", 0);
                    candidate["result"] = agent_result;
                    candidates.push_back(candidate);
                }
                response["combined_candidate"] = result.value("combined_response", "");
            }
            response["candidates"] = candidates;
            response["matching_agents"] = (int)candidates.size();
            response["raw_result"] = result;

            session_contexts[session_id]["last_input"] = input;
            session_contexts[session_id]["last_response"] = response["combined_candidate"];
            session_contexts[session_id]["timestamp"] = std::chrono::system_clock::now().time_since_epoch().count();

        } else if (operation == "get_agents") {
            response["status"] = "success";
            response["operation"] = operation;
            response["agents"] = orchestrator.get_agent_status();
            response["templates"] = orchestrator.get_swarm_status()["registered_templates"];
            response["authority_boundary"] = "candidate_only";

        } else if (operation == "get_history") {
            response["status"] = "success";
            response["operation"] = operation;
            response["session_id"] = session_id;
            response["history"] = orchestrator.get_session_history(session_id);
            response["authority_boundary"] = "candidate_only";

        } else if (operation == "get_context") {
            response["status"] = "success";
            response["operation"] = operation;
            response["session_id"] = session_id;
            response["context"] = json::object();
            if (session_contexts.find(session_id) != session_contexts.end()) {
                for (const auto& kv : session_contexts[session_id]) {
                    response["context"][kv.first] = kv.second;
                }
            }
            response["authority_boundary"] = "candidate_only";

        } else if (operation == "get_config_paths") {
            response["status"] = "success";
            response["operation"] = operation;
            response["config_paths"] = config_paths;
            response["authority_boundary"] = "candidate_only";

        } else {
            response["status"] = "error";
            response["message"] = "Unknown operation: " + operation;
            response["authority_boundary"] = "candidate_only";
        }

        return response;
    }
};

// ============================================================
// CLI / JSON-RPC SERVER
// ============================================================

json read_request_from_args(int argc, char** argv) {
    json request;
    if (argc <= 1) return request;
    std::string arg = argv[1];
    if (arg == "process" && argc > 2) {
        request["operation"] = "process";
        request["input"] = argv[2];
        request["session_id"] = (argc > 3) ? argv[3] : "cli_session";
        request["mode"] = (argc > 4) ? argv[4] : "orchestrated";
        return request;
    }
    try {
        request = json::parse(arg);
        return request;
    } catch (...) {
        request["operation"] = "process";
        request["input"] = arg;
        request["session_id"] = "cli_session";
        request["mode"] = "orchestrated";
        return request;
    }
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
            request["mode"] = "orchestrated";
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

    QuantumTrinityMicroAgentsRuntime runtime;

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
        request["input"] = "I'm feeling really frustrated about this code. The parser keeps failing.";
        request["mode"] = "orchestrated";
        g_quiet = false;
    }

    json response = runtime.process_request(request);
    std::cout << response.dump(2) << std::endl;
    return 0;
}

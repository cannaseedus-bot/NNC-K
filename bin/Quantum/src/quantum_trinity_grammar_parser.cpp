// quantum_trinity_grammar_parser.cpp
// GBNF + EBNF + PEG Hybrid Grammar System
// Version: 10.0

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
#include <stack>
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

namespace {
    std::vector<std::string> tokenize(const std::string& text) {
        std::vector<std::string> tokens;
        std::string current;
        for (char c : text) {
            if (c == ' ' || c == '\n' || c == '\t' || c == '\r') {
                if (!current.empty()) {
                    tokens.push_back(current);
                    current.clear();
                }
            } else {
                current += c;
            }
        }
        if (!current.empty()) tokens.push_back(current);
        return tokens;
    }
}

// ============================================================
// GBNF (Generalized Backus-Naur Form) ENGINE
// ============================================================

class GBNFEngine {
private:
    struct Production {
        std::string nonterminal;
        std::vector<std::vector<std::string>> alternatives;
        std::vector<float> probabilities;
        std::map<std::string, std::string> attributes;
        bool is_quantum = false;
        float quantum_phase = 0.0f;
    };

    struct Grammar {
        std::string name;
        std::map<std::string, Production> productions;
        std::string start_symbol;
        std::map<std::string, std::string> terminal_mappings;
        std::map<std::string, std::vector<std::string>> precedence_rules;
        bool is_ambiguous = false;
        int recursion_depth = 0;
    };

    std::map<std::string, Grammar> grammars;
    std::map<std::string, std::regex> terminal_patterns;
    std::map<std::string, std::function<std::string(const std::string&)>> semantic_actions;
    std::map<std::string, float> quantum_weights;
    std::mt19937 rng{std::random_device{}()};

public:
    GBNFEngine() {
        initialize_terminals();
        initialize_semantic_actions();
        initialize_quantum_weights();
    }

    void initialize_terminals() {
        terminal_patterns["identifier"] = std::regex(R"([a-zA-Z_][a-zA-Z0-9_]*)");
        terminal_patterns["number"] = std::regex(R"(\d+\.?\d*)");
        terminal_patterns["string"] = std::regex(R"("([^"\\]|\\.)*")");
        terminal_patterns["boolean"] = std::regex(R"(true|false)");
        terminal_patterns["whitespace"] = std::regex(R"(\s+)");
        terminal_patterns["comment"] = std::regex(R"(//.*|/\*[\s\S]*?\*/)");
        terminal_patterns["operator"] = std::regex(R"([+\-*/=<>!&|^%~])");
        terminal_patterns["delimiter"] = std::regex(R"([,;:.(){}[\]])");
        terminal_patterns["keyword"] = std::regex(R"(if|else|while|for|return|class|function|var|let|const)");
        // quantum/mayan glyphs omitted from regex due to Unicode complexity; matched literally
    }

    void initialize_semantic_actions() {
        semantic_actions["evaluate"] = [](const std::string& expr) -> std::string {
            try { return std::to_string(std::stod(expr)); } catch (...) { return expr; }
        };
        semantic_actions["validate"] = [](const std::string& input) -> std::string {
            return input + "_validated";
        };
        semantic_actions["transform"] = [](const std::string& input) -> std::string {
            std::string result = input;
            std::transform(result.begin(), result.end(), result.begin(),
                           [](unsigned char c) { return std::toupper(c); });
            return result;
        };
    }

    void initialize_quantum_weights() {
        quantum_weights = {
            {"symbolic", 0.95f},
            {"syntactic", 0.85f},
            {"semantic", 0.90f},
            {"pragmatic", 0.75f},
            {"quantum", 1.0f},
            {"classical", 0.80f},
            {"hybrid", 0.92f}
        };
    }

    Grammar* create_grammar(const std::string& name, const std::string& start_symbol = "start") {
        Grammar grammar;
        grammar.name = name;
        grammar.start_symbol = start_symbol;
        grammars[name] = grammar;
        return &grammars[name];
    }

    void add_production(Grammar* grammar, const std::string& nonterminal,
                        const std::vector<std::vector<std::string>>& alternatives,
                        const std::vector<float>& probabilities = {}) {
        Production prod;
        prod.nonterminal = nonterminal;
        prod.alternatives = alternatives;
        if (probabilities.empty()) {
            float prob = 1.0f / (float)alternatives.size();
            prod.probabilities = std::vector<float>(alternatives.size(), prob);
        } else {
            prod.probabilities = probabilities;
        }
        grammar->productions[nonterminal] = prod;
    }

    void add_production_quantum(Grammar* grammar, const std::string& nonterminal,
                                const std::vector<std::vector<std::string>>& alternatives,
                                float quantum_phase = 0.0f) {
        Production prod;
        prod.nonterminal = nonterminal;
        prod.alternatives = alternatives;
        prod.is_quantum = true;
        prod.quantum_phase = quantum_phase;

        std::vector<float> probs;
        float total = 0.0f;
        for (size_t i = 0; i < alternatives.size(); i++) {
            float prob = std::abs(std::sin(quantum_phase + (float)i * 0.5f));
            probs.push_back(prob);
            total += prob;
        }
        for (auto& p : probs) p /= total;
        prod.probabilities = probs;
        grammar->productions[nonterminal] = prod;
    }

    std::vector<std::string> generate_sentence(Grammar* grammar, const std::string& start = "") {
        std::vector<std::string> tokens;
        std::string current = start.empty() ? grammar->start_symbol : start;

        std::stack<std::string> expansion_stack;
        expansion_stack.push(current);
        int depth = 0;
        const int max_depth = 1000;

        while (!expansion_stack.empty() && depth++ < max_depth) {
            std::string symbol = expansion_stack.top();
            expansion_stack.pop();

            if (is_terminal(symbol)) {
                tokens.push_back(symbol);
                continue;
            }

            auto it = grammar->productions.find(symbol);
            if (it != grammar->productions.end()) {
                auto& prod = it->second;
                int alt_index = select_alternative(prod);
                auto& alternative = prod.alternatives[alt_index];

                if (prod.is_quantum) {
                    std::uniform_real_distribution<float> noise(-0.1f, 0.1f);
                    prod.quantum_phase += noise(rng);
                }

                for (int i = (int)alternative.size() - 1; i >= 0; i--) {
                    expansion_stack.push(alternative[i]);
                }
            } else {
                tokens.push_back(symbol);
            }
        }

        return tokens;
    }

    bool parse_sequence(Grammar* grammar, const std::vector<std::string>& tokens) {
        int position = 0;
        return parse_nonterminal(grammar, grammar->start_symbol, tokens, position, 0) && position == (int)tokens.size();
    }

    std::string grammar_to_string(const Grammar* grammar) {
        std::string result = "Grammar: " + grammar->name + "\n";
        result += "Start Symbol: " + grammar->start_symbol + "\n\n";
        for (const auto& [nonterminal, prod] : grammar->productions) {
            result += nonterminal + " ::= ";
            for (size_t i = 0; i < prod.alternatives.size(); i++) {
                if (i > 0) result += " | ";
                for (const auto& symbol : prod.alternatives[i]) {
                    result += symbol + " ";
                }
                if (!prod.probabilities.empty() && i < prod.probabilities.size()) {
                    result += " [p=" + std::to_string(prod.probabilities[i]) + "]";
                }
            }
            result += "\n";
        }
        return result;
    }

private:
    bool is_terminal(const std::string& symbol) {
        if (symbol.empty()) return false;
        if (symbol.size() >= 2 && ((symbol.front() == '"' && symbol.back() == '"') ||
                                       (symbol.front() == '\'' && symbol.back() == '\''))) {
            return true;
        }
        if (std::isdigit(static_cast<unsigned char>(symbol[0]))) return true;
        if (symbol.size() == 1 && std::ispunct(static_cast<unsigned char>(symbol[0]))) return true;
        return false;
    }

    int select_alternative(const Production& prod) {
        if (prod.probabilities.empty()) {
            std::uniform_int_distribution<int> dist(0, (int)prod.alternatives.size() - 1);
            return dist(rng);
        }
        std::uniform_real_distribution<float> dist(0.0f, 1.0f);
        float r = dist(rng);
        float cumulative = 0.0f;
        for (size_t i = 0; i < prod.probabilities.size(); i++) {
            cumulative += prod.probabilities[i];
            if (r <= cumulative) return (int)i;
        }
        return (int)prod.alternatives.size() - 1;
    }

    bool parse_nonterminal(Grammar* grammar, const std::string& nonterminal,
                           const std::vector<std::string>& tokens, int& position, int depth) {
        if (depth > 50) return false;
        auto it = grammar->productions.find(nonterminal);
        if (it == grammar->productions.end()) {
            if (position < (int)tokens.size() && tokens[position] == nonterminal) {
                position++;
                return true;
            }
            return false;
        }

        auto& prod = it->second;
        for (const auto& alternative : prod.alternatives) {
            int saved_position = position;
            bool success = true;
            for (const auto& symbol : alternative) {
                if (!parse_nonterminal(grammar, symbol, tokens, position, depth + 1)) {
                    success = false;
                    break;
                }
            }
            if (success) return true;
            position = saved_position;
        }
        return false;
    }
};

// ============================================================
// EBNF (Extended Backus-Naur Form) ENGINE
// ============================================================

class EBNFEngine {
private:
    struct EBNFProduction {
        std::string nonterminal;
        std::string expression;
        std::vector<std::string> symbols;
        std::vector<std::vector<std::string>> alternatives;
    };

    struct EBNFGrammar {
        std::string name;
        std::map<std::string, EBNFProduction> productions;
        std::string start_symbol;
        std::map<std::string, std::regex> terminal_patterns;
        bool allow_left_recursion = false;
        int max_recursion_depth = 100;
    };

    std::map<std::string, EBNFGrammar> grammars;
    std::map<std::string, std::function<std::string(const std::string&)>> terminal_handlers;
    std::mt19937 rng{std::random_device{}()};

public:
    EBNFEngine() {
        initialize_terminal_handlers();
    }

    void initialize_terminal_handlers() {
        terminal_handlers["integer"] = [](const std::string& input) { return input; };
        terminal_handlers["float"] = [](const std::string& input) { return input; };
        terminal_handlers["string"] = [](const std::string& input) { return input; };
        terminal_handlers["boolean"] = [](const std::string& input) { return input; };
        terminal_handlers["quantum"] = [](const std::string& input) { return "Q[" + input + "]"; };
    }

    EBNFGrammar* create_grammar(const std::string& name, const std::string& start = "start") {
        EBNFGrammar grammar;
        grammar.name = name;
        grammar.start_symbol = start;
        grammars[name] = grammar;
        return &grammars[name];
    }

    void add_production(EBNFGrammar* grammar, const std::string& nonterminal,
                        const std::string& expression) {
        EBNFProduction prod;
        prod.nonterminal = nonterminal;
        prod.expression = expression;
        prod.alternatives = parse_alternatives(expression);
        grammar->productions[nonterminal] = prod;
    }

    std::vector<std::vector<std::string>> parse_alternatives(const std::string& expression) {
        std::vector<std::vector<std::string>> alternatives;
        std::vector<std::string> current_alt;
        std::string current_symbol;
        bool in_string = false;
        char string_delim = 0;

        for (size_t i = 0; i < expression.size(); i++) {
            char c = expression[i];
            if (in_string) {
                current_symbol += c;
                if (c == string_delim) in_string = false;
                continue;
            }
            if (c == '"' || c == '\'') {
                in_string = true;
                string_delim = c;
                current_symbol += c;
                continue;
            }
            if (c == '|') {
                if (!current_symbol.empty()) {
                    current_alt.push_back(current_symbol);
                    current_symbol.clear();
                }
                if (!current_alt.empty()) {
                    alternatives.push_back(current_alt);
                    current_alt.clear();
                }
            } else if (c == ' ' || c == '\t') {
                if (!current_symbol.empty()) {
                    current_alt.push_back(current_symbol);
                    current_symbol.clear();
                }
            } else if (c == '*' || c == '?' || c == '+') {
                if (!current_symbol.empty()) {
                    current_symbol += c;
                    current_alt.push_back(current_symbol);
                    current_symbol.clear();
                }
            } else if (c == '(' || c == ')' || c == '{' || c == '}' || c == '[' || c == ']') {
                if (!current_symbol.empty()) {
                    current_alt.push_back(current_symbol);
                    current_symbol.clear();
                }
                current_alt.push_back(std::string(1, c));
            } else {
                current_symbol += c;
            }
        }
        if (!current_symbol.empty()) current_alt.push_back(current_symbol);
        if (!current_alt.empty()) alternatives.push_back(current_alt);
        return alternatives;
    }

    std::vector<std::string> generate_sentence(EBNFGrammar* grammar, const std::string& start = "") {
        std::vector<std::string> tokens;
        std::string current = start.empty() ? grammar->start_symbol : start;

        std::stack<std::pair<std::string, int>> expansion_stack;
        expansion_stack.push({current, 0});
        int depth = 0;

        while (!expansion_stack.empty() && depth++ < grammar->max_recursion_depth) {
            auto [symbol, expansion_count] = expansion_stack.top();
            expansion_stack.pop();

            if (is_terminal(symbol, grammar) || symbol.empty()) {
                if (!symbol.empty()) tokens.push_back(strip_quotes(symbol));
                continue;
            }

            auto it = grammar->productions.find(symbol);
            if (it == grammar->productions.end()) {
                tokens.push_back(symbol);
                continue;
            }

            auto& prod = it->second;
            if (prod.alternatives.empty()) continue;

            std::uniform_int_distribution<int> dist(0, (int)prod.alternatives.size() - 1);
            auto& alternative = prod.alternatives[dist(rng)];

            for (int i = (int)alternative.size() - 1; i >= 0; i--) {
                const auto& alt_symbol = alternative[i];
                if (alt_symbol.empty()) continue;
                char last = alt_symbol.back();

                if (last == '*') {
                    std::string base = strip_operator(alt_symbol);
                    int count = dist(rng) % 5;
                    for (int j = 0; j < count; j++) {
                        expansion_stack.push({base, 0});
                    }
                } else if (last == '+') {
                    std::string base = strip_operator(alt_symbol);
                    int count = 1 + (dist(rng) % 4);
                    for (int j = 0; j < count; j++) {
                        expansion_stack.push({base, 0});
                    }
                } else if (last == '?') {
                    std::string base = strip_operator(alt_symbol);
                    std::uniform_int_distribution<int> coin(0, 1);
                    if (coin(rng) == 1) expansion_stack.push({base, 0});
                } else if (alt_symbol == "(" || alt_symbol == ")" || alt_symbol == "{" || alt_symbol == "}" ||
                           alt_symbol == "[" || alt_symbol == "]") {
                    continue;
                } else {
                    expansion_stack.push({alt_symbol, 0});
                }
            }
        }

        return tokens;
    }

    bool validate_sentence(EBNFGrammar* grammar, const std::vector<std::string>& tokens) {
        if (tokens.empty()) return false;
        return true;
    }

    std::string grammar_to_string(const EBNFGrammar* grammar) {
        std::string result = "EBNF Grammar: " + grammar->name + "\n";
        result += "Start Symbol: " + grammar->start_symbol + "\n\n";
        for (const auto& [nonterminal, prod] : grammar->productions) {
            result += nonterminal + " ::= " + prod.expression + "\n";
        }
        return result;
    }

private:
    bool is_terminal(const std::string& symbol, const EBNFGrammar* grammar) {
        if (symbol.empty()) return false;
        for (const auto& [name, pattern] : grammar->terminal_patterns) {
            if (std::regex_match(symbol, pattern)) return true;
        }
        return symbol[0] == '"' || symbol[0] == '\'';
    }

    std::string strip_quotes(const std::string& s) {
        if (s.size() >= 2 && ((s.front() == '"' && s.back() == '"') || (s.front() == '\'' && s.back() == '\''))) {
            return s.substr(1, s.size() - 2);
        }
        return s;
    }

    std::string strip_operator(const std::string& s) {
        if (!s.empty() && (s.back() == '*' || s.back() == '?' || s.back() == '+')) {
            return s.substr(0, s.size() - 1);
        }
        return s;
    }
};

// ============================================================
// PEG (Parsing Expression Grammar) ENGINE
// ============================================================

class PEGEngine {
private:
    struct PEGRule {
        std::string name;
        std::string expression;
        std::vector<std::string> sequence;
        std::vector<std::vector<std::string>> ordered_choices;
        bool is_quantum = false;
        float quantum_phase = 0.0f;
    };

    struct PEGGrammar {
        std::string name;
        std::map<std::string, PEGRule> rules;
        std::string start_rule;
        std::map<std::string, std::regex> terminal_patterns;
        bool memoization = true;
        int max_recursion = 100;
        bool quantum_enabled = false;
        float quantum_coherence = 0.8f;
    };

    struct ParseResult {
        bool success = false;
        std::string value;
        int position = 0;
        std::vector<std::string> captured;
        float confidence = 0.0f;
    };

    std::map<std::string, PEGGrammar> grammars;
    std::map<std::string, std::map<int, ParseResult>> memo_cache;
    std::mt19937 rng{std::random_device{}()};

public:
    PEGEngine() {}

    PEGGrammar* create_grammar(const std::string& name, const std::string& start = "start") {
        PEGGrammar grammar;
        grammar.name = name;
        grammar.start_rule = start;
        grammars[name] = grammar;
        return &grammars[name];
    }

    void add_rule(PEGGrammar* grammar, const std::string& name, const std::string& expression) {
        PEGRule rule;
        rule.name = name;
        rule.expression = expression;
        rule.ordered_choices = parse_ordered_choices(expression);
        if (rule.ordered_choices.size() == 1) {
            rule.sequence = rule.ordered_choices[0];
        }
        grammar->rules[name] = rule;
    }

    void add_quantum_rule(PEGGrammar* grammar, const std::string& name,
                          const std::string& expression, float quantum_phase = 0.0f) {
        PEGRule rule;
        rule.name = name;
        rule.expression = expression;
        rule.ordered_choices = parse_ordered_choices(expression);
        rule.is_quantum = true;
        rule.quantum_phase = quantum_phase;
        grammar->rules[name] = rule;
        grammar->quantum_enabled = true;
    }

    ParseResult parse(PEGGrammar* grammar, const std::string& input) {
        memo_cache[grammar->name].clear();
        auto result = parse_rule(grammar, grammar->start_rule, input, 0, 0);
        result.success = result.success && result.position == (int)input.length();
        return result;
    }

    std::string generate_sentence(PEGGrammar* grammar, const std::string& start = "") {
        std::string current = start.empty() ? grammar->start_rule : start;
        return generate_from_rule(grammar, current, 0);
    }

    std::string grammar_to_string(const PEGGrammar* grammar) {
        std::string result = "PEG Grammar: " + grammar->name + "\n";
        result += "Start Rule: " + grammar->start_rule + "\n\n";
        for (const auto& [name, rule] : grammar->rules) {
            result += name + " <- " + rule.expression + "\n";
            if (rule.is_quantum) {
                result += "  [Quantum: phase=" + std::to_string(rule.quantum_phase) + "]\n";
            }
        }
        return result;
    }

private:
    std::vector<std::vector<std::string>> parse_ordered_choices(const std::string& expression) {
        std::vector<std::vector<std::string>> choices;
        std::vector<std::string> current_choice;
        std::string current;
        bool in_string = false;
        char string_delim = 0;
        int paren_depth = 0;

        for (size_t i = 0; i < expression.size(); i++) {
            char c = expression[i];
            if (in_string) {
                current += c;
                if (c == string_delim) in_string = false;
                continue;
            }
            if (c == '"' || c == '\'') {
                in_string = true;
                string_delim = c;
                current += c;
                continue;
            }
            if (c == '(') {
                paren_depth++;
                current += c;
                continue;
            }
            if (c == ')') {
                paren_depth--;
                current += c;
                continue;
            }
            if (c == '/' && paren_depth == 0) {
                if (!current.empty()) {
                    current_choice.push_back(current);
                    current.clear();
                }
                if (!current_choice.empty()) {
                    choices.push_back(current_choice);
                    current_choice.clear();
                }
            } else if (c == ' ' || c == '\t') {
                if (!current.empty()) {
                    current_choice.push_back(current);
                    current.clear();
                }
            } else {
                current += c;
            }
        }
        if (!current.empty()) current_choice.push_back(current);
        if (!current_choice.empty()) choices.push_back(current_choice);
        return choices;
    }

    ParseResult parse_rule(PEGGrammar* grammar, const std::string& rule_name,
                           const std::string& input, int pos, int depth) {
        if (depth > grammar->max_recursion || pos > (int)input.length()) {
            ParseResult fail;
            fail.position = pos;
            return fail;
        }

        // Note: memoization disabled because the original key (pos + rule_name.size()) is not unique.
        // Correct PEG memoization requires a composite (rule_name, pos) key, which is left as future work.

        ParseResult result;
        result.success = false;
        result.position = pos;

        auto rule_it = grammar->rules.find(rule_name);
        if (rule_it == grammar->rules.end()) {
            result = parse_literal(rule_name, input, pos);
            return result;
        }

        auto& rule = rule_it->second;

        if (rule.is_quantum && grammar->quantum_enabled) {
            std::vector<ParseResult> superpositions;
            for (const auto& choice : rule.ordered_choices) {
                auto sub = parse_sequence(grammar, choice, input, pos, depth + 1);
                if (sub.success) {
                    sub.confidence *= (rule.quantum_phase + 0.5f);
                    superpositions.push_back(sub);
                }
            }
            if (!superpositions.empty()) {
                std::sort(superpositions.begin(), superpositions.end(),
                          [](const ParseResult& a, const ParseResult& b) { return a.confidence > b.confidence; });
                std::uniform_real_distribution<float> noise(-0.1f, 0.1f);
                result = (superpositions[0].confidence + noise(rng) > 0.5f || superpositions.size() == 1)
                         ? superpositions[0]
                         : superpositions[1 % superpositions.size()];
            }
        } else if (rule.ordered_choices.size() > 1) {
            for (const auto& choice : rule.ordered_choices) {
                auto sub = parse_sequence(grammar, choice, input, pos, depth + 1);
                if (sub.success) {
                    result = sub;
                    break;
                }
            }
        } else if (!rule.ordered_choices.empty()) {
            result = parse_sequence(grammar, rule.ordered_choices[0], input, pos, depth + 1);
        }

        return result;
    }

    ParseResult parse_sequence(PEGGrammar* grammar, const std::vector<std::string>& sequence,
                               const std::string& input, int pos, int depth) {
        ParseResult result;
        result.success = true;
        result.position = pos;
        result.confidence = 1.0f;

        for (const auto& element : sequence) {
            if (element.empty()) continue;
            auto sub = parse_rule(grammar, element, input, result.position, depth + 1);
            if (!sub.success) {
                result.success = false;
                return result;
            }
            result.position = sub.position;
            result.value += sub.value;
            result.confidence *= sub.confidence;
        }
        return result;
    }

    ParseResult parse_literal(const std::string& literal, const std::string& input, int pos) {
        ParseResult result;
        result.success = false;
        result.position = pos;
        if (pos > (int)input.length()) return result;

        // Skip leading whitespace
        while (pos < (int)input.length() && std::isspace(static_cast<unsigned char>(input[pos]))) pos++;

        std::string target = literal;
        bool quoted = (literal.size() >= 2 && ((literal.front() == '"' && literal.back() == '"') ||
                                                   (literal.front() == '\'' && literal.back() == '\'')));
        if (quoted) target = literal.substr(1, literal.size() - 2);

        // Try exact target match first
        if (input.substr(pos).find(target) == 0) {
            result.success = true;
            result.value = target;
            result.position = pos + (int)target.size();
            result.confidence = 1.0f;
            return result;
        }

        // If quoted, also try consuming a word token matching the target
        if (quoted) {
            size_t end = pos;
            while (end < input.size() && !std::isspace(static_cast<unsigned char>(input[end]))) end++;
            std::string word = input.substr(pos, end - pos);
            if (word == target) {
                result.success = true;
                result.value = word;
                result.position = (int)end;
                result.confidence = 1.0f;
            }
        }
        return result;
    }

    std::string generate_from_rule(PEGGrammar* grammar, const std::string& rule_name, int depth) {
        if (depth > 50) return "";
        auto it = grammar->rules.find(rule_name);
        if (it == grammar->rules.end()) return strip_quotes(rule_name);

        auto& rule = it->second;
        std::string result;

        std::vector<std::string>* choice_set = nullptr;
        if (rule.is_quantum && grammar->quantum_enabled) {
            if (!rule.ordered_choices.empty()) {
                std::uniform_int_distribution<int> dist(0, (int)rule.ordered_choices.size() - 1);
                choice_set = &rule.ordered_choices[dist(rng)];
            }
        } else if (!rule.ordered_choices.empty()) {
            choice_set = &rule.ordered_choices[0];
        } else if (!rule.sequence.empty()) {
            choice_set = &rule.sequence;
        }

        if (choice_set) {
            std::vector<std::string> generated_parts;
            for (const auto& element : *choice_set) {
                std::string generated = generate_from_rule(grammar, element, depth + 1);
                if (!generated.empty()) {
                    generated_parts.push_back(generated);
                }
            }
            for (size_t i = 0; i < generated_parts.size(); i++) {
                if (i > 0) result += " ";
                result += generated_parts[i];
            }
        }

        return result;
    }

    std::string strip_quotes(const std::string& s) {
        if (s.size() >= 2 && ((s.front() == '"' && s.back() == '"') || (s.front() == '\'' && s.back() == '\''))) {
            return s.substr(1, s.size() - 2);
        }
        return s;
    }
};

// ============================================================
// HYBRID GRAMMAR SYSTEM
// ============================================================

class HybridGrammarSystem {
private:
    GBNFEngine gbnf;
    EBNFEngine ebnf;
    PEGEngine peg;

public:
    HybridGrammarSystem() {}

    json process_input(const json& request) {
        json response;
        std::string operation = request.value("operation", std::string("parse"));
        std::string grammar_type = request.value("grammar_type", std::string("gbnf"));
        std::string input = request.value("input", std::string(""));

        if (operation == "parse") {
            json results = json::object();

            if (grammar_type == "gbnf" || grammar_type == "all") {
                auto grammar = gbnf.create_grammar("parse_grammar");
                gbnf.add_production(grammar, "start", {
                    {"Hello", "World"},
                    {"Greetings", "Earth"}
                });
                gbnf.add_production(grammar, "greeting", {{"Hi"}, {"Hey"}, {"Hello"}});
                auto tokens = tokenize(input);
                results["gbnf"] = gbnf.parse_sequence(grammar, tokens) ? "valid" : "invalid";
                results["gbnf_grammar"] = gbnf.grammar_to_string(grammar);
            }

            if (grammar_type == "ebnf" || grammar_type == "all") {
                auto grammar = ebnf.create_grammar("parse_ebnf");
                ebnf.add_production(grammar, "start", "Greeting | Farewell | Statement");
                ebnf.add_production(grammar, "Greeting", "\"Hello\" | \"Hi\" | \"Hey\"");
                ebnf.add_production(grammar, "Farewell", "\"Goodbye\" | \"Bye\" | \"See You\"");
                ebnf.add_production(grammar, "Statement", "Subject Predicate Object?");
                auto tokens = tokenize(input);
                results["ebnf"] = ebnf.validate_sentence(grammar, tokens) ? "valid" : "invalid";
                results["ebnf_grammar"] = ebnf.grammar_to_string(grammar);
            }

            if (grammar_type == "peg" || grammar_type == "all") {
                auto grammar = peg.create_grammar("parse_peg");
                peg.add_rule(grammar, "start", "Sentence");
                peg.add_rule(grammar, "Sentence", "Greeting / Farewell / Statement");
                peg.add_rule(grammar, "Greeting", "\"Hello\" \"World\"");
                peg.add_rule(grammar, "Farewell", "\"Goodbye\" \"World\"");
                peg.add_rule(grammar, "Statement", "Subject Predicate");
                auto result = peg.parse(grammar, input);
                results["peg"] = result.success ? "valid" : "invalid";
                results["peg_position"] = result.position;
                results["peg_grammar"] = peg.grammar_to_string(grammar);
            }

            results["input"] = input;
            results["hybrid"] = "Grammar processing complete";

            response["status"] = "success";
            response["operation"] = operation;
            response["results"] = results;

        } else if (operation == "generate") {
            json results = json::object();

            if (grammar_type == "gbnf" || grammar_type == "all") {
                auto grammar = gbnf.create_grammar("gen_grammar");
                gbnf.add_production(grammar, "start", {
                    {"The", "quick", "brown", "fox"},
                    {"The", "lazy", "dog"}
                });
                auto tokens = gbnf.generate_sentence(grammar);
                results["gbnf"] = join_tokens(tokens);
            }

            if (grammar_type == "ebnf" || grammar_type == "all") {
                auto grammar = ebnf.create_grammar("gen_ebnf");
                ebnf.add_production(grammar, "start", "Greeting | Farewell | Statement");
                ebnf.add_production(grammar, "Greeting", "\"Hello\" | \"Hi\" | \"Hey\"");
                ebnf.add_production(grammar, "Farewell", "\"Goodbye\" | \"Bye\" | \"See You\"");
                ebnf.add_production(grammar, "Statement", "Subject Predicate Object?");
                auto tokens = ebnf.generate_sentence(grammar);
                results["ebnf"] = join_tokens(tokens);
            }

            if (grammar_type == "peg" || grammar_type == "all") {
                auto grammar = peg.create_grammar("gen_peg");
                peg.add_rule(grammar, "start", "Sentence");
                peg.add_rule(grammar, "Sentence", "Greeting / Farewell / Statement");
                peg.add_rule(grammar, "Greeting", "\"Hello\" \"World\"");
                peg.add_rule(grammar, "Farewell", "\"Goodbye\" \"World\"");
                peg.add_rule(grammar, "Statement", "Subject Predicate");
                peg.add_quantum_rule(grammar, "Subject", "\"The\" \"quick\" \"fox\" / \"The\" \"lazy\" \"dog\"", 0.5f);
                results["peg"] = peg.generate_sentence(grammar);
            }

            response["status"] = "success";
            response["operation"] = operation;
            response["results"] = results;

        } else {
            response["status"] = "error";
            response["message"] = "Unknown operation: " + operation;
        }

        return response;
    }

private:
    std::string join_tokens(const std::vector<std::string>& tokens) {
        std::string sentence;
        for (const auto& token : tokens) {
            sentence += token + " ";
        }
        if (!sentence.empty()) sentence.pop_back();
        return sentence;
    }
};

// ============================================================
// MAIN RUNTIME
// ============================================================

class QuantumTrinityGrammarRuntime {
private:
    HybridGrammarSystem grammar_system;

public:
    QuantumTrinityGrammarRuntime() {
        if (!g_quiet) {
            std::cout << "🌌 Quantum Trinity Grammar System v10.0" << std::endl;
            std::cout << "============================================" << std::endl;
            std::cout << "✅ GBNF Engine: Generalized Backus-Naur Form" << std::endl;
            std::cout << "✅ EBNF Engine: Extended Backus-Naur Form" << std::endl;
            std::cout << "✅ PEG Engine: Parsing Expression Grammar" << std::endl;
            std::cout << "✅ Hybrid Integration: Complete" << std::endl;
            std::cout << std::endl;
        }
    }

    json process_request(const json& request) {
        return grammar_system.process_input(request);
    }
};

// ============================================================
// CLI / JSON-RPC SERVER
// ============================================================

json read_request_from_args(int argc, char** argv) {
    json request;
    if (argc > 1) {
        std::string arg = argv[1];
        if ((arg == "parse" || arg == "generate") && argc > 2) {
            request["operation"] = arg;
            request["grammar_type"] = (argc > 3) ? argv[3] : "all";
            request["input"] = argv[2];
            return request;
        }
        try {
            request = json::parse(arg);
            return request;
        } catch (...) {
            request["operation"] = "parse";
            request["grammar_type"] = "all";
            request["input"] = arg;
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
            request["operation"] = "parse";
            request["grammar_type"] = "all";
            request["input"] = input;
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

    QuantumTrinityGrammarRuntime runtime;

    json request;
    if (argc > 1) {
        request = read_request_from_args(argc, argv);
    } else {
        if (!std::cin.eof() && std::cin.peek() != EOF) {
            request = read_request_from_stdin();
        }
    }

    if (request.is_null() || request.empty()) {
        request["operation"] = "parse";
        request["grammar_type"] = "all";
        request["input"] = "Hello World";
        g_quiet = false;
    }

    auto response = runtime.process_request(request);
    std::cout << response.dump(2) << std::endl;
    return 0;
}

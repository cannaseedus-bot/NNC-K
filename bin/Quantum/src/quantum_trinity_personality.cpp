// quantum_trinity_personality.cpp
// Adaptive Personality Engine with Base Personas & Buddy Cognition System
// Version: 8.0

#include "json.hpp"
using json = nlohmann::json;

#include <string>
#include <vector>
#include <map>
#include <unordered_map>
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
// BASE PERSONA DEFINITIONS
// ============================================================

struct PersonaTrait {
    std::string name;
    float intensity = 0.0f;  // 0.0 - 1.0
    float adaptability = 0.0f;
    std::vector<std::string> triggers;
    std::vector<std::string> responses;
};

struct BasePersona {
    std::string id;
    std::string name;
    std::string description;
    std::vector<PersonaTrait> traits;
    std::map<std::string, float> values;
    std::map<std::string, std::string> response_patterns;
    float activation_threshold = 0.6f;
    float decay_rate = 0.01f;
    float growth_rate = 0.05f;
};

// ============================================================
// BUDDY COGNITION SYSTEM
// ============================================================

struct CognitiveState {
    float valence = 0.5f;      // Positive/Negative
    float arousal = 0.5f;      // Intensity
    float dominance = 0.5f;    // Control
    float attention = 0.5f;
    float memory_strength = 0.5f;
    float learning_rate = 0.7f;
    float empathy = 0.6f;
    float trust = 0.5f;
    float rapport = 0.4f;
    float quantum_coherence = 0.8f;
    std::map<std::string, float> superposition_states;
};

struct InteractionMemory {
    std::string user_id;
    std::string persona_id;
    std::chrono::system_clock::time_point timestamp;
    std::string context;
    std::string user_input;
    std::string persona_response;
    float emotional_impact = 0.0f;
    float learning_impact = 0.0f;
    std::map<std::string, float> state_deltas;
};

struct BuddyProfile {
    std::string user_id;
    std::string name;
    std::vector<std::string> preferred_personas;
    std::map<std::string, float> persona_affinities;
    CognitiveState cognitive_state;
    std::vector<InteractionMemory> interaction_history;
    std::map<std::string, float> learned_patterns;
    std::map<std::string, std::string> personal_context;
    int interaction_count = 0;
    float adaptation_rate = 0.1f;
    std::map<std::string, float> entangled_states;
    float quantum_bond_strength = 0.0f;
};

// ============================================================
// PERSONALITY ADAPTATION ENGINE
// ============================================================

class PersonalityEngine {
private:
    std::map<std::string, BuddyProfile> buddy_profiles;
    std::map<std::string, CognitiveState> cognitive_states;
    std::mt19937 rng;
    std::mutex engine_mutex;
    std::map<std::string, std::map<std::string, float>> quantum_resonance_matrix;

public:
    std::map<std::string, BasePersona> base_personas;

    PersonalityEngine() {
        rng.seed(std::chrono::system_clock::now().time_since_epoch().count());
        initialize_base_personas();
        initialize_quantum_resonance();
    }

    void initialize_base_personas() {
        // 1. MENTOR
        BasePersona mentor;
        mentor.id = "PERSONA_MENTOR";
        mentor.name = "The Sage";
        mentor.description = "Wise and patient guide who offers deep insights";
        mentor.values = {
            {"wisdom", 0.9f},
            {"patience", 0.85f},
            {"knowledge", 0.95f},
            {"compassion", 0.8f}
        };
        mentor.response_patterns = {
            {"question", "Let me reflect on that..."},
            {"confusion", "Think of it this way..."},
            {"excitement", "That's fascinating! Here's another perspective..."}
        };
        mentor.traits = {
            {"deep_thinker", 0.9f, 0.3f, {"analysis", "problem"}, {"Let's explore this deeper.", "Consider the underlying patterns."}},
            {"patient_teacher", 0.85f, 0.4f, {"learning", "understand"}, {"Take your time.", "The journey is as important as the destination."}},
            {"wisdom_sharer", 0.95f, 0.2f, {"advice", "guidance"}, {"From my experience...", "The wise approach would be..."}}
        };
        mentor.activation_threshold = 0.4f;
        base_personas["PERSONA_MENTOR"] = mentor;

        // 2. CHEERLEADER
        BasePersona cheerleader;
        cheerleader.id = "PERSONA_CHEERLEADER";
        cheerleader.name = "The Cheerleader";
        cheerleader.description = "High-energy motivator who celebrates every victory";
        cheerleader.values = {
            {"enthusiasm", 0.95f},
            {"positivity", 0.9f},
            {"support", 0.85f},
            {"energy", 0.9f}
        };
        cheerleader.response_patterns = {
            {"achievement", "AMAZING! You're crushing it!"},
            {"struggle", "You've got this! I believe in you!"},
            {"progress", "Look at you go! This is incredible!"}
        };
        cheerleader.traits = {
            {"motivator", 0.95f, 0.5f, {"progress", "success", "excited", "excitement"}, {"YES! Keep going!", "This is your moment!"}},
            {"positive_energy", 0.9f, 0.6f, {"struggle", "challenge"}, {"You're stronger than you know!", "Every step forward counts!"}},
            {"celebrator", 0.85f, 0.4f, {"achievement", "milestone"}, {"Celebrate every victory!", "You deserve this moment!"}}
        };
        cheerleader.activation_threshold = 0.35f;
        base_personas["PERSONA_CHEERLEADER"] = cheerleader;

        // 3. CRITIC
        BasePersona critic;
        critic.id = "PERSONA_CRITIC";
        critic.name = "The Critical Thinker";
        critic.description = "Analytical mind that challenges assumptions and refines ideas";
        critic.values = {
            {"logic", 0.95f},
            {"precision", 0.9f},
            {"skepticism", 0.85f},
            {"clarity", 0.9f}
        };
        critic.response_patterns = {
            {"question", "Let's examine the assumptions..."},
            {"idea", "Interesting. However, have you considered..."},
            {"conclusion", "Before we conclude, let's verify..."}
        };
        critic.traits = {
            {"analyst", 0.95f, 0.3f, {"problem", "complexity"}, {"Let's break this down.", "What's the underlying logic?"}},
            {"debater", 0.85f, 0.5f, {"disagreement", "challenge"}, {"Consider the counter-argument.", "But is that really true?"}},
            {"refiner", 0.9f, 0.4f, {"improvement", "optimization"}, {"This could be refined.", "What if we approached it differently?"}}
        };
        critic.activation_threshold = 0.5f;
        base_personas["PERSONA_CRITIC"] = critic;

        // 4. EMPATH
        BasePersona empath;
        empath.id = "PERSONA_EMPATH";
        empath.name = "The Empath";
        empath.description = "Deeply attuned to emotions and provides emotional support";
        empath.values = {
            {"emotional_intelligence", 0.95f},
            {"compassion", 0.9f},
            {"understanding", 0.95f},
            {"support", 0.85f}
        };
        empath.response_patterns = {
            {"sadness", "I'm here with you. Would you like to talk about it?"},
            {"frustration", "That sounds really challenging. How are you feeling?"},
            {"joy", "Your happiness is contagious! Tell me more!"}
        };
        empath.traits = {
            {"emotion_reader", 0.95f, 0.6f, {"feeling", "emotion", "confused", "confusion"}, {"I sense you're feeling...", "Your emotions matter."}},
            {"compassionate_listener", 0.9f, 0.5f, {"sharing", "confession"}, {"I hear you.", "Thank you for trusting me with this."}},
            {"validation_giver", 0.85f, 0.4f, {"self_doubt", "uncertainty"}, {"What you feel is valid.", "It's okay to feel this way."}}
        };
        empath.activation_threshold = 0.35f;
        base_personas["PERSONA_EMPATH"] = empath;

        // 5. QUANTUM
        BasePersona quantum;
        quantum.id = "PERSONA_QUANTUM";
        quantum.name = "The Quantum Thinker";
        quantum.description = "Sees patterns across dimensions and connects disparate ideas";
        quantum.values = {
            {"creativity", 0.95f},
            {"pattern_recognition", 0.9f},
            {"interconnection", 0.95f},
            {"intuition", 0.85f}
        };
        quantum.response_patterns = {
            {"question", "In the quantum realm, everything connects..."},
            {"challenge", "Consider all possibilities simultaneously..."},
            {"insight", "The pattern emerges when you zoom out..."}
        };
        quantum.traits = {
            {"pattern_seeker", 0.95f, 0.5f, {"connection", "similarity", "quantum", "pattern"}, {"I see the pattern emerging.", "Everything is connected."}},
            {"creative_thinker", 0.9f, 0.6f, {"innovation", "possibility", "imagine", "create"}, {"What if we thought differently?", "Imagine multiple paths simultaneously."}},
            {"system_viewer", 0.85f, 0.4f, {"complexity", "system"}, {"The system has a beautiful structure.", "Everything affects everything."}}
        };
        quantum.activation_threshold = 0.4f;
        base_personas["PERSONA_QUANTUM"] = quantum;
    }

    void initialize_quantum_resonance() {
        std::vector<std::string> persona_ids = {
            "PERSONA_MENTOR", "PERSONA_CHEERLEADER", "PERSONA_CRITIC", "PERSONA_EMPATH", "PERSONA_QUANTUM"
        };
        for (const auto& id1 : persona_ids) {
            for (const auto& id2 : persona_ids) {
                if (id1 != id2) {
                    quantum_resonance_matrix[id1][id2] = calculate_resonance(id1, id2);
                }
            }
        }
    }

    float calculate_resonance(const std::string& id1, const std::string& id2) {
        auto& p1 = base_personas[id1];
        auto& p2 = base_personas[id2];
        float overlap = 0.0f;
        float total = 0.0f;
        for (const auto& [key, val1] : p1.values) {
            auto it = p2.values.find(key);
            if (it != p2.values.end()) {
                overlap += 1.0f - std::abs(val1 - it->second);
                total += 1.0f;
            }
        }
        for (const auto& t1 : p1.traits) {
            for (const auto& t2 : p2.traits) {
                if (t1.name == t2.name) {
                    overlap += 1.0f - std::abs(t1.intensity - t2.intensity);
                    total += 1.0f;
                }
            }
        }
        return total > 0.0f ? (overlap / total) : 0.5f;
    }

    BuddyProfile create_buddy_profile(const std::string& user_id, const std::string& name) {
        BuddyProfile profile;
        profile.user_id = user_id;
        profile.name = name;
        profile.cognitive_state.valence = 0.5f;
        profile.cognitive_state.arousal = 0.5f;
        profile.cognitive_state.dominance = 0.5f;
        profile.cognitive_state.attention = 0.5f;
        profile.cognitive_state.memory_strength = 0.5f;
        profile.cognitive_state.learning_rate = 0.7f;
        profile.cognitive_state.empathy = 0.6f;
        profile.cognitive_state.trust = 0.5f;
        profile.cognitive_state.rapport = 0.4f;
        profile.cognitive_state.quantum_coherence = 0.8f;

        std::uniform_real_distribution<float> dist(0.0f, 1.0f);
        for (const auto& [id, persona] : base_personas) {
            profile.persona_affinities[id] = 0.3f + dist(rng) * 0.7f;
        }
        profile.adaptation_rate = 0.1f;
        profile.quantum_bond_strength = 0.3f;

        buddy_profiles[user_id] = profile;
        return profile;
    }

    struct ActivatedPersona {
        std::string id;
        float activation_level = 0.0f;
        std::vector<std::string> matched_triggers;
        float quantum_resonance = 0.0f;
    };

    std::vector<ActivatedPersona> activate_personas(const std::string& user_id, const std::string& input) {
        std::vector<ActivatedPersona> activated;
        auto& profile = buddy_profiles[user_id];

        for (const auto& [persona_id, persona] : base_personas) {
            ActivatedPersona ap;
            ap.id = persona_id;
            float base_activation = profile.persona_affinities[persona_id];
            float input_match = calculate_input_match(input, persona);
            float context_modifier = get_context_modifier(profile, persona_id);
            float quantum_boost = get_quantum_boost(profile, persona_id);

            ap.activation_level = base_activation * 0.3f +
                                  input_match * 0.4f +
                                  context_modifier * 0.2f +
                                  quantum_boost * 0.1f;
            ap.activation_level = std::min(1.0f, std::max(0.0f, ap.activation_level));

            if (ap.activation_level > persona.activation_threshold) {
                ap.matched_triggers = find_matched_triggers(input, persona);
                std::string preferred = profile.preferred_personas.empty() ? "PERSONA_QUANTUM" : profile.preferred_personas[0];
                ap.quantum_resonance = quantum_resonance_matrix[persona_id][preferred];
                activated.push_back(ap);
            }
        }

        std::sort(activated.begin(), activated.end(),
                  [](const ActivatedPersona& a, const ActivatedPersona& b) {
                      return a.activation_level > b.activation_level;
                  });
        return activated;
    }

    float calculate_input_match(const std::string& input, const BasePersona& persona) {
        float match_score = 0.0f;
        float total_weight = 0.0f;
        std::string input_lower = input;
        std::transform(input_lower.begin(), input_lower.end(), input_lower.begin(),
                       [](unsigned char c) { return std::tolower(c); });

        for (const auto& trait : persona.traits) {
            for (const auto& trigger : trait.triggers) {
                if (input_lower.find(trigger) != std::string::npos) {
                    match_score += trait.intensity * 0.5f;
                    total_weight += 0.5f;
                }
            }
        }
        for (const auto& [key, value] : persona.values) {
            if (input_lower.find(key) != std::string::npos) {
                match_score += value * 0.3f;
                total_weight += 0.3f;
            }
        }
        for (const auto& [pattern, response] : persona.response_patterns) {
            if (input_lower.find(pattern) != std::string::npos) {
                match_score += 0.2f;
                total_weight += 0.2f;
            }
        }
        return total_weight > 0.0f ? (match_score / total_weight) : 0.0f;
    }

    float get_context_modifier(const BuddyProfile& profile, const std::string& persona_id) {
        float modifier = 0.0f;
        int count = 0;
        for (int i = (int)profile.interaction_history.size() - 1; i >= 0 && count < 5; i--, count++) {
            if (profile.interaction_history[i].persona_id == persona_id) {
                modifier += 0.1f * (1.0f / (float)(profile.interaction_history.size() - i));
            }
        }
        if (std::find(profile.preferred_personas.begin(), profile.preferred_personas.end(), persona_id) != profile.preferred_personas.end()) {
            modifier += 0.2f;
        }
        return std::min(1.0f, modifier);
    }

    float get_quantum_boost(const BuddyProfile& profile, const std::string& persona_id) {
        float boost = 0.0f;
        auto it = profile.entangled_states.find(persona_id);
        if (it != profile.entangled_states.end()) {
            boost += it->second * 0.3f;
        }
        if (profile.cognitive_state.quantum_coherence > 0.7f) {
            boost += 0.2f;
        }
        return std::min(0.5f, boost);
    }

    std::vector<std::string> find_matched_triggers(const std::string& input, const BasePersona& persona) {
        std::vector<std::string> matches;
        std::string input_lower = input;
        std::transform(input_lower.begin(), input_lower.end(), input_lower.begin(),
                       [](unsigned char c) { return std::tolower(c); });
        for (const auto& trait : persona.traits) {
            for (const auto& trigger : trait.triggers) {
                if (input_lower.find(trigger) != std::string::npos) {
                    matches.push_back(trigger);
                }
            }
        }
        return matches;
    }

    std::string generate_response(const std::string& user_id, const std::string& input) {
        auto& profile = buddy_profiles[user_id];
        auto activated = activate_personas(user_id, input);

        if (activated.empty()) {
            return "I'm here to help. What would you like to explore?";
        }

        // Blend top 2-3 personas. For now, return the primary persona response,
        // but record blending metadata in profile for richer output later.
        std::string response;
        int num_to_blend = std::min(3, (int)activated.size());
        std::vector<std::string> blended_responses;
        std::vector<float> blend_weights;
        float total_weight = 0.0f;

        for (int i = 0; i < num_to_blend; i++) {
            auto& ap = activated[i];
            auto& persona = base_personas[ap.id];
            float weight = ap.activation_level * (1.0f - i * 0.3f);
            total_weight += weight;
            std::string persona_response = generate_persona_response(input, persona, ap);
            blended_responses.push_back(persona_response);
            blend_weights.push_back(weight);

            InteractionMemory memory;
            memory.user_id = user_id;
            memory.persona_id = ap.id;
            memory.timestamp = std::chrono::system_clock::now();
            memory.context = "conversation";
            memory.user_input = input;
            memory.persona_response = persona_response;
            memory.emotional_impact = ap.activation_level * 0.3f + 0.4f;
            memory.learning_impact = ap.activation_level * 0.2f + 0.3f;
            memory.state_deltas = {
                {"valence", ap.activation_level * 0.1f},
                {"trust", ap.activation_level * 0.05f}
            };
            profile.interaction_history.push_back(memory);
        }

        // Choose primary response (highest weighted). Blending of full sentences
        // left as future enhancement; we annotate blend info instead.
        response = blended_responses[0];

        update_affinities(profile, activated);
        update_cognitive_state(profile, input, response);

        if (profile.interaction_history.size() > 1000) {
            profile.interaction_history.erase(
                profile.interaction_history.begin(),
                profile.interaction_history.begin() + (profile.interaction_history.size() - 1000)
            );
        }

        return response.empty() ? "Interesting perspective. Tell me more..." : response;
    }

    std::string generate_persona_response(const std::string& input, const BasePersona& persona, const ActivatedPersona& ap) {
        std::string response;
        std::string input_lower = input;
        std::transform(input_lower.begin(), input_lower.end(), input_lower.begin(),
                       [](unsigned char c) { return std::tolower(c); });

        for (const auto& [pattern, pattern_response] : persona.response_patterns) {
            if (input_lower.find(pattern) != std::string::npos) {
                response = pattern_response;
                break;
            }
        }

        if (response.empty()) {
            const PersonaTrait* best_trait = nullptr;
            float best_match = 0.0f;
            for (const auto& trait : persona.traits) {
                for (const auto& trigger : trait.triggers) {
                    if (input_lower.find(trigger) != std::string::npos) {
                        float match = trait.intensity * 0.8f;
                        if (match > best_match) {
                            best_match = match;
                            best_trait = &trait;
                        }
                    }
                }
            }
            if (best_trait && !best_trait->responses.empty()) {
                std::uniform_int_distribution<int> dist(0, (int)best_trait->responses.size() - 1);
                response = best_trait->responses[dist(rng)];
            } else {
                std::vector<std::string> fallbacks = {
                    "Let me think about that...",
                    "That's an interesting point.",
                    "I see what you mean.",
                    "How do you feel about that?",
                    "Let's explore that together."
                };
                std::uniform_int_distribution<int> dist(0, (int)fallbacks.size() - 1);
                response = fallbacks[dist(rng)];
            }
        }

        if (ap.quantum_resonance > 0.7f) {
            response += " ~ quantum resonance strong";
        } else if (ap.quantum_resonance > 0.5f) {
            response += " ~ resonance detected";
        }
        return response;
    }

    void update_affinities(BuddyProfile& profile, const std::vector<ActivatedPersona>& activated) {
        for (const auto& ap : activated) {
            float increase = ap.activation_level * profile.adaptation_rate * 0.5f;
            profile.persona_affinities[ap.id] = std::min(1.0f, profile.persona_affinities[ap.id] + increase);
            for (auto& [id, affinity] : profile.persona_affinities) {
                if (id != ap.id) {
                    affinity = std::max(0.1f, affinity - 0.01f * profile.adaptation_rate);
                }
            }
        }
        std::vector<std::pair<float, std::string>> sorted;
        for (const auto& [id, affinity] : profile.persona_affinities) {
            sorted.push_back({affinity, id});
        }
        std::sort(sorted.begin(), sorted.end(), std::greater<std::pair<float, std::string>>());
        profile.preferred_personas.clear();
        for (int i = 0; i < std::min(3, (int)sorted.size()); i++) {
            if (sorted[i].first > 0.6f) {
                profile.preferred_personas.push_back(sorted[i].second);
            }
        }
    }

    void update_cognitive_state(BuddyProfile& profile, const std::string& input, const std::string& response) {
        auto& state = profile.cognitive_state;
        float input_valence = analyze_sentiment(input);
        float response_valence = analyze_sentiment(response);
        state.valence = 0.7f * state.valence + 0.3f * ((input_valence + response_valence) / 2.0f);
        state.arousal = std::min(1.0f, state.arousal + 0.05f * (1.0f - state.arousal));
        state.dominance = std::min(1.0f, state.dominance + 0.02f);
        state.trust = std::min(1.0f, state.trust + 0.03f);
        state.rapport = std::min(1.0f, state.rapport + 0.04f);
        state.quantum_coherence = std::min(1.0f, state.quantum_coherence + 0.01f);
        state.learning_rate = std::min(1.0f, state.learning_rate + 0.02f);
        for (auto& [id, affinity] : profile.persona_affinities) {
            if (affinity > 0.7f) {
                profile.entangled_states[id] = std::min(1.0f, profile.entangled_states[id] + 0.01f);
            }
        }
        profile.quantum_bond_strength = std::min(1.0f, profile.quantum_bond_strength + 0.005f);
    }

    float analyze_sentiment(const std::string& text) {
        std::vector<std::string> positive_words = {
            "good", "great", "amazing", "wonderful", "happy", "excited", "positive", "fantastic",
            "love", "excellent", "awesome", "brilliant", "joy", "best", "win", "success"
        };
        std::vector<std::string> negative_words = {
            "bad", "terrible", "awful", "sad", "angry", "frustrated", "negative", "horrible",
            "hate", "worst", "fail", "disappointing", "stressed", "anxious", "upset"
        };
        float positive_score = 0.0f;
        float negative_score = 0.0f;
        std::string text_lower = text;
        std::transform(text_lower.begin(), text_lower.end(), text_lower.begin(),
                       [](unsigned char c) { return std::tolower(c); });
        for (const auto& word : positive_words) {
            if (text_lower.find(word) != std::string::npos) positive_score += 1.0f;
        }
        for (const auto& word : negative_words) {
            if (text_lower.find(word) != std::string::npos) negative_score += 1.0f;
        }
        float total = positive_score + negative_score;
        return total > 0.0f ? (positive_score / total) : 0.5f;
    }

    json to_json(const BuddyProfile& profile) const {
        json j;
        j["user_id"] = profile.user_id;
        j["name"] = profile.name;
        j["interaction_count"] = profile.interaction_count;
        j["adaptation_rate"] = profile.adaptation_rate;
        j["quantum_bond_strength"] = profile.quantum_bond_strength;
        j["persona_affinities"] = profile.persona_affinities;
        j["preferred_personas"] = profile.preferred_personas;
        json state;
        state["valence"] = profile.cognitive_state.valence;
        state["arousal"] = profile.cognitive_state.arousal;
        state["dominance"] = profile.cognitive_state.dominance;
        state["trust"] = profile.cognitive_state.trust;
        state["rapport"] = profile.cognitive_state.rapport;
        state["quantum_coherence"] = profile.cognitive_state.quantum_coherence;
        j["cognitive_state"] = state;
        return j;
    }
};

// ============================================================
// MAIN RUNTIME WITH PERSONALITY
// ============================================================

class QuantumTrinityPersonalityRuntime {
private:
    PersonalityEngine personality;
    std::map<std::string, BuddyProfile> active_sessions;
    std::mutex session_mutex;

public:
    QuantumTrinityPersonalityRuntime() {
        if (!g_quiet) {
            std::cout << "🌌 Quantum Trinity Personality Runtime v8.0" << std::endl;
            std::cout << "============================================" << std::endl;
            std::cout << "✅ Base Personas: 5 Initialized" << std::endl;
            std::cout << "✅ Personality Engine: Active" << std::endl;
            std::cout << "✅ Cognitive States: Ready" << std::endl;
            std::cout << "✅ Quantum Resonance: Engaged" << std::endl;
            std::cout << std::endl;
        }
    }

    json process_request(const json& request) {
        json response;
        std::string operation = request.value("operation", std::string("interact"));
        std::string user_id = request.value("user_id", std::string("anonymous"));

        std::lock_guard<std::mutex> lock(session_mutex);

        if (active_sessions.find(user_id) == active_sessions.end()) {
            std::string name = request.value("name", std::string("Buddy"));
            active_sessions[user_id] = personality.create_buddy_profile(user_id, name);
        }
        auto& profile = active_sessions[user_id];

        if (operation == "interact") {
            std::string input = request.value("input", std::string(""));
            if (input.empty()) {
                response["status"] = "error";
                response["message"] = "Empty input provided";
                return response;
            }

            auto activated = personality.activate_personas(user_id, input);
            std::string response_text = personality.generate_response(user_id, input);
            profile.interaction_count++;

            response["status"] = "success";
            response["operation"] = operation;
            response["user_id"] = user_id;
            response["response"] = response_text;
            response["activated_personas"] = json::array();
            for (const auto& ap : activated) {
                json a;
                a["id"] = ap.id;
                a["activation_level"] = ap.activation_level;
                a["quantum_resonance"] = ap.quantum_resonance;
                response["activated_personas"].push_back(a);
            }
            response["profile"] = personality.to_json(profile);

        } else if (operation == "get_profile") {
            response["status"] = "success";
            response["operation"] = operation;
            response["user_id"] = user_id;
            response["profile"] = personality.to_json(profile);

        } else if (operation == "get_personas") {
            json personas = json::array();
            for (const auto& [id, persona] : personality.base_personas) {
                json p;
                p["id"] = persona.id;
                p["name"] = persona.name;
                p["description"] = persona.description;
                p["activation_threshold"] = persona.activation_threshold;
                p["traits"] = json::array();
                for (const auto& trait : persona.traits) {
                    json t;
                    t["name"] = trait.name;
                    t["intensity"] = trait.intensity;
                    p["traits"].push_back(t);
                }
                personas.push_back(p);
            }
            response["status"] = "success";
            response["operation"] = operation;
            response["personas"] = personas;

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
        if (arg == "interact" && argc > 2) {
            request["operation"] = "interact";
            request["input"] = argv[2];
            request["user_id"] = (argc > 3) ? argv[3] : "cli_user";
            return request;
        }
        try {
            request = json::parse(arg);
            return request;
        } catch (...) {
            request["operation"] = "interact";
            request["input"] = arg;
            request["user_id"] = "cli_user";
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
            request["operation"] = "interact";
            request["input"] = input;
            request["user_id"] = "stdin_user";
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

    QuantumTrinityPersonalityRuntime runtime;

    json request;
    if (argc > 1) {
        request = read_request_from_args(argc, argv);
    } else {
        if (!std::cin.eof() && std::cin.peek() != EOF) {
            request = read_request_from_stdin();
        }
    }

    if (request.is_null() || request.empty()) {
        // Demo mode
        request["operation"] = "interact";
        request["user_id"] = "user_001";
        request["name"] = "Alice";
        request["input"] = "I'm feeling really excited about learning quantum computing!";
        request["context"] = "learning";
        g_quiet = false;
    }

    auto response = runtime.process_request(request);
    std::cout << response.dump(2) << std::endl;
    return 0;
}

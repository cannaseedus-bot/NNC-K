cmake_minimum_required(VERSION 3.20)
project(quantum_trinity)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Output directories
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin")

if(MSVC)
    add_definitions(-D_CRT_SECURE_NO_WARNINGS)
endif()

include_directories("${CMAKE_SOURCE_DIR}/include")

add_executable(quantum_trinity
    src/quantum_trinity_web_research.cpp
)

target_link_libraries(quantum_trinity PRIVATE winhttp)

add_executable(quantum_personality
    src/quantum_trinity_personality.cpp
)

target_link_libraries(quantum_personality PRIVATE winhttp)

add_executable(quantum_hybrid
    src/quantum_trinity_hybrid.cpp
)

target_link_libraries(quantum_hybrid PRIVATE winhttp)

add_executable(quantum_grammar
    src/quantum_trinity_grammar_parser.cpp
)

target_link_libraries(quantum_grammar PRIVATE winhttp)

add_executable(quantum_microagents
    src/quantum_trinity_micro_agents.cpp
)

# quantum_microagents performs local pattern inference only; no HTTP needed.

add_executable(asx_ram_v2
    src/quantum_trinity_asx_ram_v2.cpp
)

target_link_libraries(asx_ram_v2 PRIVATE d3d11 d3dcompiler)

add_executable(asx_gemm
    src/quantum_trinity_asx_gemm.cpp
)

target_link_libraries(asx_gemm PRIVATE d3d11 d3dcompiler)


cmake_minimum_required(VERSION 3.16)
project(GW2ProximityChatServer VERSION 1.0.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_AUTOMOC ON)

find_package(Qt6 REQUIRED COMPONENTS Core WebSockets)

configure_file(version.h.in version.h)

add_executable(GW2ProximityChatServer
    main.cpp
    config.cpp
    server.cpp
)

target_include_directories(GW2ProximityChatServer PRIVATE
    ${CMAKE_CURRENT_BINARY_DIR}
)

target_link_libraries(GW2ProximityChatServer PRIVATE
    Qt6::Core
    Qt6::WebSockets
)

if(WIN32)
    set_target_properties(GW2ProximityChatServer PROPERTIES WIN32_EXECUTABLE FALSE)
endif()

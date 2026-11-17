cmake_minimum_required(VERSION 3.16)
project(shutdown_probe LANGUAGES CXX)
add_library(shutdown_probe SHARED shutdown_probe.cpp)
set_target_properties(shutdown_probe PROPERTIES CXX_STANDARD 11)

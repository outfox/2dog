cmake_minimum_required(VERSION 3.21)
project(twodog_qt LANGUAGES CXX)

find_package(Qt6 6.8 REQUIRED COMPONENTS Quick)
qt_standard_project_setup(REQUIRES 6.8)

# The module lands in qml/TwoDog; the package targets copy it next to the app, which is on
# QML's default import path.
set(QT_QML_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/qml)

# The plugin is its own backing library: C# loads the same file to reach the C exports.
qt_add_qml_module(twodogqt
    URI TwoDog
    VERSION 1.0
    PLUGIN_TARGET twodogqt
    SOURCES godotview.h godotview.cpp
)
target_link_libraries(twodogqt PRIVATE Qt6::Quick)
if(WIN32)
    target_link_libraries(twodogqt PRIVATE d3d11 dxgi)
endif()

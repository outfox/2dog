import QtQuick
import QtQuick.Controls.Basic
import QtQuick.Layouts
import TwoDog

ApplicationWindow {
    width: 1152
    height: 720
    minimumWidth: 480
    minimumHeight: 320
    visible: true
    title: "TPLRAWNAME — Qt Quick host"
    color: "#1E2633"

    GameControls {
        id: controls
    }

    // The engine's viewport; it takes keyboard focus so Godot receives key input.
    GodotView {
        anchors.fill: parent
        focus: true
    }

    // The overlay floats over the running game - the point of this host.
    Rectangle {
        anchors.top: parent.top
        anchors.right: parent.right
        anchors.margins: 16
        width: 280
        height: panel.implicitHeight + 32
        radius: 14
        color: "#E6121821"
        border.color: "#2EFFFFFF"

        ColumnLayout {
            id: panel
            anchors.fill: parent
            anchors.margins: 16
            spacing: 14

            RowLayout {
                ColumnLayout {
                    spacing: 2
                    Layout.fillWidth: true
                    Label {
                        text: "TPLRAWNAME"
                        font.pixelSize: 17
                        font.bold: true
                        color: "white"
                        elide: Text.ElideRight
                        Layout.fillWidth: true
                    }
                    Label { text: "Qt Quick controls over Godot"; font.pixelSize: 12; color: "#9AA7B8" }
                }
                Rectangle {
                    implicitWidth: status.implicitWidth + 20
                    implicitHeight: status.implicitHeight + 8
                    radius: 12
                    color: "#1AFFFFFF"
                    RowLayout {
                        id: status
                        anchors.centerIn: parent
                        spacing: 6
                        Rectangle { width: 8; height: 8; radius: 4; color: controls.statusColor }
                        Label { text: controls.status; font.pixelSize: 12; color: controls.statusColor }
                    }
                }
            }

            Button {
                id: pause
                Layout.fillWidth: true
                implicitHeight: 40
                text: controls.paused ? "Resume" : "Pause"
                enabled: controls.running
                onClicked: controls.togglePause()
                contentItem: Label {
                    text: pause.text
                    font.bold: true
                    color: "#0B1220"
                    horizontalAlignment: Text.AlignHCenter
                    verticalAlignment: Text.AlignVCenter
                }
                background: Rectangle {
                    radius: 8
                    color: pause.down ? "#9BD462" : pause.hovered ? "#BDF088" : "#A8E66A"
                    opacity: pause.enabled ? 1 : 0.45
                }
            }
        }
    }
}

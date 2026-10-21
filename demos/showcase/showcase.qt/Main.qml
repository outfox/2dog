import QtQuick
import QtQuick.Controls.Basic
import QtQuick.Layouts
import TwoDog

ApplicationWindow {
    width: 1152
    height: 720
    minimumWidth: 480
    minimumHeight: 360
    visible: true
    title: "2dog showcase — Qt Quick host"
    color: "#1E2633"

    ShowcaseControls {
        id: controls
    }

    GodotView {
        anchors.fill: parent
        focus: true
    }

    // The point of this host: ordinary Qt Quick items floating over the game.
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
            spacing: 12

            RowLayout {
                ColumnLayout {
                    spacing: 2
                    Label { text: "2dog showcase"; font.pixelSize: 17; font.bold: true; color: "white" }
                    Label { text: "Qt Quick controls over Godot"; font.pixelSize: 12; color: "#9AA7B8" }
                }
                Item { Layout.fillWidth: true }
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

            Rectangle { Layout.fillWidth: true; height: 1; color: "#24FFFFFF" }

            ColumnLayout {
                spacing: 0
                RowLayout {
                    Label { text: "Game speed"; font.pixelSize: 13; color: "white"; Layout.fillWidth: true }
                    Label { text: speed.value.toFixed(2) + "×"; font.pixelSize: 13; color: "white" }
                }
                Slider {
                    id: speed
                    Layout.fillWidth: true
                    from: 0
                    to: 3
                    value: 1
                    enabled: controls.running
                    onMoved: controls.setSpeed(value)
                    Accessible.name: "Game speed"
                    background: Rectangle {
                        x: speed.leftPadding
                        y: speed.topPadding + speed.availableHeight / 2 - height / 2
                        width: speed.availableWidth
                        height: 4
                        radius: 2
                        color: "#33FFFFFF"
                        Rectangle {
                            width: speed.visualPosition * parent.width
                            height: parent.height
                            radius: 2
                            color: "#A8E66A"
                        }
                    }
                    handle: Rectangle {
                        x: speed.leftPadding + speed.visualPosition * (speed.availableWidth - width)
                        y: speed.topPadding + speed.availableHeight / 2 - height / 2
                        width: 18
                        height: 18
                        radius: 9
                        color: speed.pressed ? "#9BD462" : "#A8E66A"
                        opacity: speed.enabled ? 1 : 0.45
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

            Label { text: controls.mode; font.pixelSize: 12; color: "#9AA7B8" }
            Label { text: controls.fps; font.pixelSize: 12; color: "#9AA7B8"; visible: text !== "" }
        }
    }
}

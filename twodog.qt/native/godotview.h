#pragma once

#include <QImage>
#include <QQuickItem>
#include <QtQml/qqmlregistration.h>

#include <cstdint>
#include <memory>

extern "C" {
// Mirrored by twodog.qt's Native.cs; keep both layouts in sync.
struct TwoDogQtInput
{
    int32_t type;
    int32_t button;
    int32_t buttons;
    int32_t modifiers;
    double x;
    double y;
    double deltaX;
    double deltaY;
    int32_t key;
    int32_t unicode;
    int32_t autoRepeat;
    uint32_t scanCode;
};

struct TwoDogQtCallbacks
{
    void (*attached)(void *view);
    void (*detached)(void *view);
    // Once per Qt Quick frame of an exposed window, on the GUI thread. False: request no next frame (the engine is
    // mid-iteration, or not presenting into this view).
    bool (*frame)(void *view);
    // A steady background beat on the GUI thread, which keeps the engine running without frames.
    void (*tick)();
    void (*input)(void *view, const TwoDogQtInput *input);
    void (*shutdown)();
};
}

enum TwoDogQtInputType : int32_t
{
    MouseMove,
    MousePress,
    MouseRelease,
    MouseDoubleClick,
    Wheel,
    KeyPress,
    KeyRelease,
    FocusOut,
};

// Shared textures of one size that the engine writes on the GUI thread and Qt samples on the render thread.
struct GpuSet;

// Shows frames that 2dog.qt pushes from the engine and forwards input back to it. The item
// paces the engine: each Qt Quick frame on the GUI thread pumps one engine frame.
class GodotView : public QQuickItem
{
    Q_OBJECT
    QML_ELEMENT

public:
    explicit GodotView(QQuickItem *parent = nullptr);
    ~GodotView() override;

    QSize pixelSize() const;
    bool isExposed() const;
    int graphicsApi(quint64 *adapterLuid) const;
    void present(const void *rgba, int width, int height);

    int gpuCreate(int width, int height, bool allowNtHandles, bool opaque, uint64_t *handles, bool *ntHandles);
    int gpuBeginWrite();
    void gpuEndWrite(int index, bool written);
    void gpuDestroy();
    bool gpuFailed() const;

protected:
    QSGNode *updatePaintNode(QSGNode *node, UpdatePaintNodeData *data) override;
    void itemChange(ItemChange change, const ItemChangeData &value) override;
    void mousePressEvent(QMouseEvent *event) override;
    void mouseReleaseEvent(QMouseEvent *event) override;
    void mouseDoubleClickEvent(QMouseEvent *event) override;
    void mouseMoveEvent(QMouseEvent *event) override;
    void hoverMoveEvent(QHoverEvent *event) override;
    void wheelEvent(QWheelEvent *event) override;
    void keyPressEvent(QKeyEvent *event) override;
    void keyReleaseEvent(QKeyEvent *event) override;
    void focusOutEvent(QFocusEvent *event) override;

private:
    void setWindow(QQuickWindow *window);
    void pump();
    void sendMouse(int32_t type, const QSinglePointEvent *event);
    void sendKey(int32_t type, QKeyEvent *event);
    void send(const TwoDogQtInput &input);

    QQuickWindow *m_window = nullptr;
    QMetaObject::Connection m_frameConnection;
    QImage m_frame;
    bool m_frameDirty = false;
    std::shared_ptr<GpuSet> m_gpu;
};

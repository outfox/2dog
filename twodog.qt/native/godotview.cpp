#include "godotview.h"

#include <QCoreApplication>
#include <QCursor>
#include <QMutex>
#include <QQuickWindow>
#include <QSGRendererInterface>
#include <QSGSimpleTextureNode>
#include <QSGTexture>
#include <QtQuick/qsgtexture_platform.h>
#include <QThread>
#include <QTimer>

#if defined(Q_OS_WIN)
#  include <d3d11_1.h>
#  include <dxgi1_2.h>
#endif

#include <array>
#include <atomic>

namespace
{
TwoDogQtCallbacks g_callbacks{};
std::atomic<bool> g_hasCallbacks{false};

const TwoDogQtCallbacks *callbacks()
{
    return g_hasCallbacks.load(std::memory_order_acquire) ? &g_callbacks : nullptr;
}

#if defined(Q_OS_WIN)
template <class T>
void releaseCom(T *&object)
{
    if (object) {
        object->Release();
        object = nullptr;
    }
}

// AcquireSync reports contention as WAIT_TIMEOUT, which some layers wrap as an HRESULT.
bool isTimeout(HRESULT hr)
{
    return hr == static_cast<HRESULT>(WAIT_TIMEOUT) || hr == HRESULT_FROM_WIN32(WAIT_TIMEOUT);
}

ID3D11Device *qtDevice(QQuickWindow *window)
{
    auto *renderer = window->rendererInterface();
    if (renderer->graphicsApi() != QSGRendererInterface::Direct3D11)
        return nullptr;
    return static_cast<ID3D11Device *>(renderer->getResource(window, QSGRendererInterface::DeviceResource));
}
#endif
}

#if defined(Q_OS_WIN)
// Three keyed-mutex textures, created on a writer device on Qt's adapter and opened on Qt's own device. The engine
// imports each one into its Vulkan device. The keyed mutex hands a buffer between the sides (key 0: writer, key 1:
// reader); the engine's copy stalls until it has finished on the GPU, and a buffer only goes back to the writer once
// an event query shows Qt's GPU work with it completed. The writer device's context is used on the GUI thread only,
// Qt's on the render thread only.
struct GpuSet
{
    enum State { Free, Writing, Ready, Displayed, Retiring };

    struct Buffer
    {
        ID3D11Texture2D *writer = nullptr;
        IDXGIKeyedMutex *writerMutex = nullptr;
        ID3D11Texture2D *reader = nullptr;
        IDXGIKeyedMutex *readerMutex = nullptr;
        ID3D11Query *lastUse = nullptr;
        HANDLE handle = nullptr;
        State state = Free;
    };

    QMutex lock;
    QSize size;
    bool ntHandles = false;
    bool opaque = true;
    bool failed = false;
    int ready = -1;
    int displayed = -1;
    int acquired = -1;
    ID3D11Device *writerDevice = nullptr;
    ID3D11Device *readerDevice = nullptr;
    std::array<Buffer, 3> buffers;

    ~GpuSet()
    {
        for (auto &buffer : buffers) {
            releaseCom(buffer.lastUse);
            releaseCom(buffer.readerMutex);
            releaseCom(buffer.reader);
            releaseCom(buffer.writerMutex);
            releaseCom(buffer.writer);
            // Neither Qt's nor the engine's import takes ownership of an NT handle.
            if (ntHandles && buffer.handle)
                CloseHandle(buffer.handle);
        }
        releaseCom(readerDevice);
        releaseCom(writerDevice);
    }
};
#else
struct GpuSet
{
};
#endif

// Lives on the render thread. Shows either the last CPU-uploaded frame or the displayed buffer of a GPU set.
class GodotNode : public QSGSimpleTextureNode
{
public:
    explicit GodotNode(QQuickWindow *window)
        : m_window(window)
    {
        setFiltering(QSGTexture::Linear);
#if defined(Q_OS_WIN)
        // Direct3D 11 runs Qt's recorded commands when the frame ends, so the reader holds its key from before
        // rendering until after the frame.
        m_hooks = new QObject;
        QObject::connect(window, &QQuickWindow::beforeRendering, m_hooks, [this] { acquire(); },
                         Qt::DirectConnection);
        QObject::connect(window, &QQuickWindow::afterFrameEnd, m_hooks, [this] { release(); },
                         Qt::DirectConnection);
#endif
    }

    ~GodotNode() override
    {
        delete m_hooks;
#if defined(Q_OS_WIN)
        if (m_set) {
            QMutexLocker locker(&m_set->lock);
            if (m_set->acquired >= 0)
                m_set->buffers[m_set->acquired].readerMutex->ReleaseSync(1);
            m_set->acquired = -1;
        }
#endif
        qDeleteAll(m_gpuTextures);
        delete m_cpuTexture;
    }

    void showCpu(QSGTexture *texture)
    {
        setTexture(texture);
        delete m_cpuTexture;
        m_cpuTexture = texture;
        dropGpu();
    }

    void showGpu(const std::shared_ptr<GpuSet> &set);

private:
    void dropGpu()
    {
        qDeleteAll(m_gpuTextures);
        m_gpuTextures = {};
        m_set.reset();
    }

#if defined(Q_OS_WIN)
    void acquire();
    void release();
#endif

    QQuickWindow *m_window;
    QObject *m_hooks = nullptr;
    QSGTexture *m_cpuTexture = nullptr;
    std::shared_ptr<GpuSet> m_set;
    std::array<QSGTexture *, 3> m_gpuTextures{};
};

#if defined(Q_OS_WIN)
void GodotNode::showGpu(const std::shared_ptr<GpuSet> &set)
{
    // A new set (resize) replaces the shown one once it has a frame; until then the old frame stretches.
    if (set != m_set) {
        QMutexLocker locker(&set->lock);
        if (set->ready < 0)
            return;
    }
    auto previous = m_gpuTextures;
    if (set != m_set) {
        m_gpuTextures = {};
        m_set = set;
    } else {
        previous = {};
    }

    QMutexLocker locker(&m_set->lock);
    if (m_set->ready >= 0) {
        if (m_set->displayed >= 0)
            m_set->buffers[m_set->displayed].state = GpuSet::Retiring;
        m_set->displayed = m_set->ready;
        m_set->buffers[m_set->displayed].state = GpuSet::Displayed;
        m_set->ready = -1;
    }
    if (m_set->displayed >= 0) {
        auto &texture = m_gpuTextures[m_set->displayed];
        if (!texture) {
            texture = QNativeInterface::QSGD3D11Texture::fromNative(
                m_set->buffers[m_set->displayed].reader, m_window, m_set->size,
                m_set->opaque ? QQuickWindow::CreateTextureOptions() : QQuickWindow::TextureHasAlphaChannel);
        }
        setTexture(texture);
        delete m_cpuTexture;
        m_cpuTexture = nullptr;
    }
    qDeleteAll(previous);
}

void GodotNode::acquire()
{
    if (!m_set)
        return;
    QMutexLocker locker(&m_set->lock);
    if (m_set->displayed < 0 || m_set->acquired >= 0)
        return;
    // The writer released key 1 before marking the buffer ready, so this returns at once unless the device failed.
    if (m_set->buffers[m_set->displayed].readerMutex->AcquireSync(1, 1000) == S_OK)
        m_set->acquired = m_set->displayed;
    else
        m_set->failed = true;
}

void GodotNode::release()
{
    if (!m_set)
        return;
    QMutexLocker locker(&m_set->lock);
    ID3D11DeviceContext *context = nullptr;
    m_set->readerDevice->GetImmediateContext(&context);
    if (m_set->acquired >= 0) {
        auto &buffer = m_set->buffers[m_set->acquired];
        buffer.readerMutex->ReleaseSync(1);
        m_set->acquired = -1;
        releaseCom(buffer.lastUse);
        D3D11_QUERY_DESC desc{D3D11_QUERY_EVENT, 0};
        if (SUCCEEDED(m_set->readerDevice->CreateQuery(&desc, &buffer.lastUse)))
            context->End(buffer.lastUse);
    }
    for (auto &buffer : m_set->buffers) {
        if (buffer.state != GpuSet::Retiring)
            continue;
        if (buffer.lastUse) {
            const HRESULT done = context->GetData(buffer.lastUse, nullptr, 0, D3D11_ASYNC_GETDATA_DONOTFLUSH);
            if (FAILED(done))
                m_set->failed = true;
            if (done != S_OK)
                continue;
        }
        const HRESULT hr = buffer.readerMutex->AcquireSync(1, 0);
        if (isTimeout(hr))
            continue;
        if (hr != S_OK || FAILED(buffer.readerMutex->ReleaseSync(0))) {
            m_set->failed = true;
            continue;
        }
        releaseCom(buffer.lastUse);
        buffer.state = GpuSet::Free;
    }
    context->Release();
}
#else
void GodotNode::showGpu(const std::shared_ptr<GpuSet> &)
{
}
#endif

GodotView::GodotView(QQuickItem *parent)
    : QQuickItem(parent)
{
    setFlag(ItemHasContents);
    setAcceptedMouseButtons(Qt::AllButtons);
    setAcceptHoverEvents(true);
    setActiveFocusOnTab(true);
}

GodotView::~GodotView()
{
    setWindow(nullptr);
}

QSize GodotView::pixelSize() const
{
    const qreal scale = m_window ? m_window->effectiveDevicePixelRatio() : 1.0;
    return QSize(qRound(width() * scale), qRound(height() * scale));
}

bool GodotView::isExposed() const
{
    return m_window && m_window->isExposed();
}

// The scene graph backend (QSGRendererInterface::GraphicsApi), or -1 before the scene graph is initialized. For
// Direct3D 11 the adapter's LUID is reported too, so the engine can render on the same GPU.
int GodotView::graphicsApi(quint64 *adapterLuid) const
{
    *adapterLuid = 0;
    if (!m_window || !m_window->isSceneGraphInitialized())
        return -1;
#if defined(Q_OS_WIN)
    if (auto *device = qtDevice(m_window)) {
        IDXGIDevice *dxgiDevice = nullptr;
        IDXGIAdapter *adapter = nullptr;
        IDXGIAdapter1 *adapter1 = nullptr;
        DXGI_ADAPTER_DESC1 desc{};
        // A software adapter (WARP) has no Vulkan counterpart to share textures with.
        if (SUCCEEDED(device->QueryInterface(IID_PPV_ARGS(&dxgiDevice)))
            && SUCCEEDED(dxgiDevice->GetAdapter(&adapter))
            && SUCCEEDED(adapter->QueryInterface(IID_PPV_ARGS(&adapter1)))
            && SUCCEEDED(adapter1->GetDesc1(&desc)) && !(desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE)) {
            *adapterLuid = (quint64(quint32(desc.AdapterLuid.HighPart)) << 32) | desc.AdapterLuid.LowPart;
        }
        releaseCom(adapter1);
        releaseCom(adapter);
        releaseCom(dxgiDevice);
    }
#endif
    return m_window->rendererInterface()->graphicsApi();
}

void GodotView::present(const void *rgba, int width, int height)
{
    // Godot composes 2D with premultiplied math, so the readback is premultiplied as well.
    m_frame = QImage(static_cast<const uchar *>(rgba), width, height, width * 4,
                     QImage::Format_RGBA8888_Premultiplied).copy();
    m_frameDirty = true;
}

// Returns the number of shared textures (their handles in handles[]), or 0 when Qt's backend cannot share them.
// Opaque: the engine's alpha channel is not meaningful (no transparent background), so Qt must not blend with it.
int GodotView::gpuCreate(int width, int height, bool allowNtHandles, bool opaque, uint64_t *handles,
                         bool *ntHandles)
{
#if defined(Q_OS_WIN)
    auto *readerDevice = m_window ? qtDevice(m_window) : nullptr;
    if (!readerDevice)
        return 0;
    auto set = std::make_shared<GpuSet>();
    set->size = QSize(width, height);
    set->opaque = opaque;
    readerDevice->AddRef();
    set->readerDevice = readerDevice;

    if (m_gpu && m_gpu->writerDevice) {
        set->writerDevice = m_gpu->writerDevice;
        set->writerDevice->AddRef();
    } else {
        IDXGIDevice *dxgiDevice = nullptr;
        IDXGIAdapter *adapter = nullptr;
        HRESULT hr = readerDevice->QueryInterface(IID_PPV_ARGS(&dxgiDevice));
        if (SUCCEEDED(hr))
            hr = dxgiDevice->GetAdapter(&adapter);
        // A specific adapter requires D3D_DRIVER_TYPE_UNKNOWN.
        if (SUCCEEDED(hr)) {
            hr = D3D11CreateDevice(adapter, D3D_DRIVER_TYPE_UNKNOWN, nullptr, 0, nullptr, 0, D3D11_SDK_VERSION,
                                   &set->writerDevice, nullptr, nullptr);
        }
        releaseCom(adapter);
        releaseCom(dxgiDevice);
        if (FAILED(hr))
            return 0;
    }

    // NT handles are preferred: some drivers import only those.
    ID3D11Device1 *readerDevice1 = nullptr;
    set->ntHandles = allowNtHandles && SUCCEEDED(readerDevice->QueryInterface(IID_PPV_ARGS(&readerDevice1)));

    D3D11_TEXTURE2D_DESC desc{};
    desc.Width = UINT(width);
    desc.Height = UINT(height);
    desc.MipLevels = 1;
    desc.ArraySize = 1;
    desc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    desc.SampleDesc.Count = 1;
    desc.Usage = D3D11_USAGE_DEFAULT;
    desc.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
    desc.MiscFlags = D3D11_RESOURCE_MISC_SHARED_KEYEDMUTEX
                     | (set->ntHandles ? D3D11_RESOURCE_MISC_SHARED_NTHANDLE : 0);

    bool ok = true;
    for (size_t i = 0; ok && i < set->buffers.size(); ++i) {
        auto &buffer = set->buffers[i];
        ok = SUCCEEDED(set->writerDevice->CreateTexture2D(&desc, nullptr, &buffer.writer))
             && SUCCEEDED(buffer.writer->QueryInterface(IID_PPV_ARGS(&buffer.writerMutex)));
        if (ok && set->ntHandles) {
            IDXGIResource1 *resource = nullptr;
            ok = SUCCEEDED(buffer.writer->QueryInterface(IID_PPV_ARGS(&resource)))
                 && SUCCEEDED(resource->CreateSharedHandle(
                     nullptr, DXGI_SHARED_RESOURCE_READ | DXGI_SHARED_RESOURCE_WRITE, nullptr, &buffer.handle))
                 && SUCCEEDED(readerDevice1->OpenSharedResource1(buffer.handle, IID_PPV_ARGS(&buffer.reader)));
            releaseCom(resource);
        } else if (ok) {
            IDXGIResource *resource = nullptr;
            ok = SUCCEEDED(buffer.writer->QueryInterface(IID_PPV_ARGS(&resource)))
                 && SUCCEEDED(resource->GetSharedHandle(&buffer.handle))
                 && SUCCEEDED(readerDevice->OpenSharedResource(buffer.handle, IID_PPV_ARGS(&buffer.reader)));
            releaseCom(resource);
        }
        ok = ok && SUCCEEDED(buffer.reader->QueryInterface(IID_PPV_ARGS(&buffer.readerMutex)));
        handles[i] = reinterpret_cast<uint64_t>(buffer.handle);
    }
    releaseCom(readerDevice1);
    if (!ok)
        return 0;
    *ntHandles = set->ntHandles;
    m_gpu = std::move(set);
    return int(m_gpu->buffers.size());
#else
    Q_UNUSED(width);
    Q_UNUSED(height);
    Q_UNUSED(allowNtHandles);
    Q_UNUSED(opaque);
    Q_UNUSED(handles);
    Q_UNUSED(ntHandles);
    return 0;
#endif
}

// The index of a buffer the engine may write now, -1 while Qt still uses all of them, -2 when sharing failed.
int GodotView::gpuBeginWrite()
{
#if defined(Q_OS_WIN)
    if (!m_gpu)
        return -2;
    QMutexLocker locker(&m_gpu->lock);
    if (m_gpu->failed)
        return -2;
    for (size_t i = 0; i < m_gpu->buffers.size(); ++i) {
        auto &buffer = m_gpu->buffers[i];
        if (buffer.state != GpuSet::Free)
            continue;
        const HRESULT hr = buffer.writerMutex->AcquireSync(0, 0);
        if (isTimeout(hr))
            continue;
        if (hr != S_OK) {
            m_gpu->failed = true;
            return -2;
        }
        buffer.state = GpuSet::Writing;
        return int(i);
    }
    return -1;
#else
    return -2;
#endif
}

void GodotView::gpuEndWrite(int index, bool written)
{
#if defined(Q_OS_WIN)
    if (!m_gpu || index < 0 || index >= int(m_gpu->buffers.size()))
        return;
    QMutexLocker locker(&m_gpu->lock);
    auto &buffer = m_gpu->buffers[index];
    if (FAILED(buffer.writerMutex->ReleaseSync(written ? 1 : 0))) {
        m_gpu->failed = true;
        return;
    }
    if (!written) {
        buffer.state = GpuSet::Free;
        return;
    }
    // A frame Qt never showed goes back to the writer through the reader, like any shown one.
    if (m_gpu->ready >= 0)
        m_gpu->buffers[m_gpu->ready].state = GpuSet::Retiring;
    m_gpu->ready = index;
    buffer.state = GpuSet::Ready;
    update();
#else
    Q_UNUSED(index);
    Q_UNUSED(written);
#endif
}

void GodotView::gpuDestroy()
{
    m_gpu.reset();
    update();
}

bool GodotView::gpuFailed() const
{
#if defined(Q_OS_WIN)
    if (!m_gpu)
        return false;
    QMutexLocker locker(&m_gpu->lock);
    return m_gpu->failed;
#else
    return false;
#endif
}

QSGNode *GodotView::updatePaintNode(QSGNode *oldNode, UpdatePaintNodeData *)
{
    auto *node = static_cast<GodotNode *>(oldNode);
    if (!node)
        node = new GodotNode(window());
    if (m_gpu) {
        node->showGpu(m_gpu);
    } else if (m_frameDirty && !m_frame.isNull()) {
        // shortcut: uploads a fresh texture per frame; fine for the CPU fallback, the GPU path shares textures.
        node->showCpu(window()->createTextureFromImage(m_frame));
    }
    m_frameDirty = false;
    if (!node->texture()) {
        delete node;
        return nullptr;
    }
    node->setRect(boundingRect());
    return node;
}

void GodotView::itemChange(ItemChange change, const ItemChangeData &value)
{
    if (change == ItemSceneChange)
        setWindow(value.window);
    QQuickItem::itemChange(change, value);
}

void GodotView::setWindow(QQuickWindow *window)
{
    if (window == m_window)
        return;
    if (m_window) {
        QObject::disconnect(m_frameConnection);
        if (const auto *cb = callbacks())
            cb->detached(this);
        // Shared textures belong to the old window's device.
        m_gpu.reset();
    }
    m_window = window;
    if (!m_window)
        return;
    // afterAnimating runs on the GUI thread once per scene graph frame, ahead of the sync that
    // calls updatePaintNode: the engine frame produced there is the one this Qt frame shows.
    m_frameConnection = connect(m_window, &QQuickWindow::afterAnimating, this, &GodotView::pump);
    if (const auto *cb = callbacks())
        cb->attached(this);
    update();
}

void GodotView::pump()
{
    const auto *cb = callbacks();
    // Godot's message pump dispatches Qt's messages too, so Qt renders frames inside an engine iteration. Those
    // nested frames must not request more, or the engine's message loop never drains.
    if (cb && !cb->frame(this))
        return;
    // The item update syncs new content. The window update keeps frames coming, and with them the engine pumped
    // at the display rate: the item's alone is a no-op while it is still dirty from before this frame's sync.
    update();
    m_window->update();
}

void GodotView::send(const TwoDogQtInput &input)
{
    if (const auto *cb = callbacks())
        cb->input(this, &input);
}

void GodotView::sendMouse(int32_t type, const QSinglePointEvent *event)
{
    const qreal scale = m_window ? m_window->effectiveDevicePixelRatio() : 1.0;
    TwoDogQtInput input{};
    input.type = type;
    input.button = static_cast<int32_t>(event->button());
    input.buttons = static_cast<int32_t>(event->buttons());
    input.modifiers = static_cast<int32_t>(event->modifiers());
    input.x = event->position().x() * scale;
    input.y = event->position().y() * scale;
    send(input);
}

void GodotView::sendKey(int32_t type, QKeyEvent *event)
{
    TwoDogQtInput input{};
    input.type = type;
    input.modifiers = static_cast<int32_t>(event->modifiers());
    input.key = event->key();
    const QString text = event->text();
    input.unicode = text.isEmpty() ? 0 : static_cast<int32_t>(text.toUcs4().value(0));
    input.autoRepeat = event->isAutoRepeat();
    input.scanCode = event->nativeScanCode();
    send(input);
    event->accept();
}

void GodotView::mousePressEvent(QMouseEvent *event)
{
    forceActiveFocus(Qt::MouseFocusReason);
    sendMouse(MousePress, event);
    event->accept();
}

void GodotView::mouseReleaseEvent(QMouseEvent *event)
{
    sendMouse(MouseRelease, event);
    event->accept();
}

void GodotView::mouseDoubleClickEvent(QMouseEvent *event)
{
    sendMouse(MouseDoubleClick, event);
    event->accept();
}

void GodotView::mouseMoveEvent(QMouseEvent *event)
{
    sendMouse(MouseMove, event);
    event->accept();
}

void GodotView::hoverMoveEvent(QHoverEvent *event)
{
    sendMouse(MouseMove, event);
    event->accept();
}

void GodotView::wheelEvent(QWheelEvent *event)
{
    const qreal scale = m_window ? m_window->effectiveDevicePixelRatio() : 1.0;
    TwoDogQtInput input{};
    input.type = Wheel;
    input.buttons = static_cast<int32_t>(event->buttons());
    input.modifiers = static_cast<int32_t>(event->modifiers());
    input.x = event->position().x() * scale;
    input.y = event->position().y() * scale;
    // One notch reports 120 eighths of a degree; Godot's wheel factor counts notches.
    input.deltaX = event->angleDelta().x() / 120.0;
    input.deltaY = event->angleDelta().y() / 120.0;
    send(input);
    event->accept();
}

void GodotView::keyPressEvent(QKeyEvent *event)
{
    sendKey(KeyPress, event);
}

void GodotView::keyReleaseEvent(QKeyEvent *event)
{
    sendKey(KeyRelease, event);
}

void GodotView::focusOutEvent(QFocusEvent *event)
{
    TwoDogQtInput input{};
    input.type = FocusOut;
    send(input);
    QQuickItem::focusOutEvent(event);
}

extern "C" {

Q_DECL_EXPORT void twodog_qt_set_callbacks(const TwoDogQtCallbacks *callbacks)
{
    if (!callbacks) {
        g_hasCallbacks.store(false, std::memory_order_release);
        return;
    }
    g_callbacks = *callbacks;
    g_hasCallbacks.store(true, std::memory_order_release);

    static std::atomic<bool> connected{false};
    if (!connected.exchange(true)) {
        QObject::connect(qApp, &QCoreApplication::aboutToQuit, qApp, [] {
            if (const auto *cb = ::callbacks())
                cb->shutdown();
        });
        // Created on the GUI thread. Qt Quick renders no frames for a window that is not exposed (minimized,
        // hidden), so this beat keeps the engine running as long as the application's event loop does.
        QMetaObject::invokeMethod(qApp, [] {
            auto *timer = new QTimer(qApp);
            QObject::connect(timer, &QTimer::timeout, [] {
                if (const auto *cb = ::callbacks())
                    cb->tick();
            });
            timer->start(1000 / 30);
        }, Qt::QueuedConnection);
    }
}

Q_DECL_EXPORT void twodog_qt_view_pixel_size(void *view, int32_t *width, int32_t *height)
{
    const QSize size = static_cast<GodotView *>(view)->pixelSize();
    *width = size.width();
    *height = size.height();
}

Q_DECL_EXPORT bool twodog_qt_view_exposed(void *view)
{
    return static_cast<GodotView *>(view)->isExposed();
}

Q_DECL_EXPORT int32_t twodog_qt_view_graphics_api(void *view, uint64_t *adapterLuid)
{
    quint64 luid = 0;
    const int api = static_cast<GodotView *>(view)->graphicsApi(&luid);
    *adapterLuid = luid;
    return api;
}

Q_DECL_EXPORT void twodog_qt_view_present(void *view, const void *rgba, int32_t width, int32_t height)
{
    static_cast<GodotView *>(view)->present(rgba, width, height);
}

Q_DECL_EXPORT void twodog_qt_view_set_cursor(void *view, int32_t shape)
{
    static_cast<GodotView *>(view)->setCursor(QCursor(static_cast<Qt::CursorShape>(shape)));
}

Q_DECL_EXPORT int32_t twodog_qt_gpu_create(void *view, int32_t width, int32_t height, bool allowNtHandles,
                                           bool opaque, uint64_t *handles, bool *ntHandles)
{
    return static_cast<GodotView *>(view)->gpuCreate(width, height, allowNtHandles, opaque, handles, ntHandles);
}

Q_DECL_EXPORT int32_t twodog_qt_gpu_begin_write(void *view)
{
    return static_cast<GodotView *>(view)->gpuBeginWrite();
}

Q_DECL_EXPORT void twodog_qt_gpu_end_write(void *view, int32_t index, bool written)
{
    static_cast<GodotView *>(view)->gpuEndWrite(index, written);
}

Q_DECL_EXPORT void twodog_qt_gpu_destroy(void *view)
{
    static_cast<GodotView *>(view)->gpuDestroy();
}

Q_DECL_EXPORT bool twodog_qt_gpu_failed(void *view)
{
    return static_cast<GodotView *>(view)->gpuFailed();
}

Q_DECL_EXPORT void twodog_qt_post(void (*callback)(void *), void *state)
{
    QMetaObject::invokeMethod(qApp, [callback, state] { callback(state); }, Qt::QueuedConnection);
}

Q_DECL_EXPORT bool twodog_qt_is_gui_thread()
{
    return QThread::isMainThread();
}

Q_DECL_EXPORT void twodog_qt_quit(int32_t code)
{
    QMetaObject::invokeMethod(qApp, [code] { QCoreApplication::exit(code); }, Qt::QueuedConnection);
}

}

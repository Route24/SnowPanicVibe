import Cocoa
import UserNotifications

// ─────────────────────────────────────────
// MARK: - Entry Point
// ─────────────────────────────────────────

let app = NSApplication.shared
app.setActivationPolicy(.accessory)
let delegate = AppDelegate()
app.delegate = delegate
app.run()

// ─────────────────────────────────────────
// MARK: - AppDelegate
// ─────────────────────────────────────────

class AppDelegate: NSObject, NSApplicationDelegate {
    var window: NSWindow!
    var lightView: LightView!
    var timer: Timer!
    var lastRaw = ""

    let watchedFile = "/Users/kenichinishi/unity/SnowPanicVibe/Assets/Logs/compile_status.json"

    func applicationDidFinishLaunching(_ notification: Notification) {
        // 通知許可
        UNUserNotificationCenter.current()
            .requestAuthorization(options: [.sound, .alert]) { _, _ in }

        // ウィンドウ
        window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 80, height: 80),
            styleMask: [.borderless],
            backing: .buffered,
            defer: false
        )
        window.isOpaque = false
        window.backgroundColor = .clear
        window.level = .floating
        window.collectionBehavior = [.canJoinAllSpaces, .stationary]
        window.isMovableByWindowBackground = true
        window.hasShadow = false

        // 画面右上に配置
        if let screen = NSScreen.main {
            let x = screen.visibleFrame.maxX - 100
            let y = screen.visibleFrame.maxY - 100
            window.setFrameOrigin(NSPoint(x: x, y: y))
        }

        // ライトビュー
        lightView = LightView(frame: NSRect(x: 0, y: 0, width: 80, height: 80))
        window.contentView = lightView
        window.makeKeyAndOrderFront(nil)

        // ポーリング開始
        timer = Timer.scheduledTimer(withTimeInterval: 0.8, repeats: true) { [weak self] _ in
            self?.poll()
        }
        RunLoop.main.add(timer, forMode: .common)
        poll() // 初回即時
    }

    func poll() {
        let url = URL(fileURLWithPath: watchedFile)
        guard let data = try? Data(contentsOf: url),
              let json = try? JSONSerialization.jsonObject(with: data) as? [String: String],
              let raw  = json["status"] else {
            DispatchQueue.main.async { self.lightView.setStatus("unknown") }
            return
        }
        let at = json["updatedAt"] ?? ""
        DispatchQueue.main.async {
            let changed = raw != self.lastRaw
            self.lastRaw = raw
            self.lightView.setStatus(raw)
            if changed && raw == "done" {
                self.notify("コンパイル完了", body: at)
            }
        }
    }

    func notify(_ title: String, body: String) {
        let c = UNMutableNotificationContent()
        c.title = title
        c.body  = body
        c.sound = .default
        UNUserNotificationCenter.current()
            .add(UNNotificationRequest(identifier: UUID().uuidString,
                                       content: c, trigger: nil))
    }
}

// ─────────────────────────────────────────
// MARK: - LightView (純AppKit描画)
// ─────────────────────────────────────────

class LightView: NSView {
    private var status = "unknown"

    // 状態に対応する色
    private var lightColor: NSColor {
        switch status {
        case "compiling": return NSColor(red: 1.0,  green: 0.22, blue: 0.22, alpha: 1)
        case "done":      return NSColor(red: 0.22, green: 0.55, blue: 1.0,  alpha: 1)
        case "error":     return NSColor(red: 1.0,  green: 0.60, blue: 0.10, alpha: 1)
        default:          return NSColor(white: 0.40, alpha: 1)
        }
    }

    private var labelText: String {
        switch status {
        case "compiling": return "..."
        case "done":      return "OK"
        case "error":     return "ERR"
        default:          return "?"
        }
    }

    func setStatus(_ s: String) {
        status = s
        needsDisplay = true
    }

    override var isFlipped: Bool { true }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override func draw(_ dirtyRect: NSRect) {
        // 背景: 半透明の丸角矩形
        let bg = NSBezierPath(roundedRect: bounds.insetBy(dx: 4, dy: 4),
                              xRadius: 14, yRadius: 14)
        NSColor(white: 0.08, alpha: 0.72).setFill()
        bg.fill()

        let cx = bounds.midX
        let cy = bounds.midY - 6

        // グロー（ぼかし円）
        let glowR: CGFloat = 28
        let glowRect = NSRect(x: cx - glowR, y: cy - glowR, width: glowR*2, height: glowR*2)
        if let shadow = NSShadow() as NSShadow? {
            shadow.shadowColor = lightColor.withAlphaComponent(0.6)
            shadow.shadowBlurRadius = 12
            shadow.shadowOffset = .zero
            shadow.set()
        }
        let circle = NSBezierPath(ovalIn: glowRect.insetBy(dx: 8, dy: 8))
        lightColor.withAlphaComponent(0.25).setFill()
        circle.fill()
        NSShadow().set() // shadow リセット

        // メイン円
        let r: CGFloat = 18
        let mainRect = NSRect(x: cx - r, y: cy - r, width: r*2, height: r*2)
        let mainCircle = NSBezierPath(ovalIn: mainRect)

        // グラデーション（上が明るく、下がやや暗め）
        let bright = lightColor.blended(withFraction: 0.25, of: .white) ?? lightColor
        let dark   = lightColor.blended(withFraction: 0.20, of: .black) ?? lightColor
        if let grad = NSGradient(starting: bright, ending: dark) {
            grad.draw(in: mainCircle, angle: -90)
        } else {
            lightColor.setFill()
            mainCircle.fill()
        }

        // ハイライト（上部の白い反射）
        let hlRect = NSRect(x: cx - 7, y: cy - r + 3, width: 14, height: 8)
        let hl = NSBezierPath(ovalIn: hlRect)
        NSColor(white: 1.0, alpha: 0.38).setFill()
        hl.fill()

        // ラベル
        let attrs: [NSAttributedString.Key: Any] = [
            .font: NSFont.monospacedSystemFont(ofSize: 9, weight: .bold),
            .foregroundColor: NSColor(white: 0.9, alpha: 1)
        ]
        let str = NSAttributedString(string: labelText, attributes: attrs)
        let sw = str.size().width
        str.draw(at: NSPoint(x: cx - sw/2, y: bounds.maxY - 18))
    }
}

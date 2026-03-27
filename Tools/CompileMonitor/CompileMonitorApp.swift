// CompileMonitorApp.swift
// compile_status.json を監視してコンパイル状態をデスクトップに表示する小型アプリ。
// Swift Package Manager で1ファイルビルド可能。
//
// ビルド: swift CompileMonitorApp.swift  は不可。
// 下記「ビルド手順」参照。

import SwiftUI
import AppKit
import UserNotifications

// ─────────────────────────────────────────
// MARK: - Entry Point
// ─────────────────────────────────────────

@main
struct CompileMonitorApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) var appDelegate

    var body: some Scene {
        // SwiftUI の Window は使わず AppDelegate で NSWindow を直接管理
        Settings { EmptyView() }
    }
}

// ─────────────────────────────────────────
// MARK: - AppDelegate
// ─────────────────────────────────────────

class AppDelegate: NSObject, NSApplicationDelegate {
    var window: NSWindow!

    func applicationDidFinishLaunching(_ notification: Notification) {
        // 通知許可（完了時の1回通知に使用）
        UNUserNotificationCenter.current().requestAuthorization(options: [.sound, .alert]) { _, _ in }

        let contentView = MonitorView()
        let hosting = NSHostingView(rootView: contentView)

        window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 80, height: 80),
            styleMask: [.borderless, .fullSizeContentView],
            backing: .buffered,
            defer: false
        )
        window.contentView = hosting
        window.isOpaque = false
        window.backgroundColor = .clear
        window.level = .floating          // 常に最前面
        window.collectionBehavior = [.canJoinAllSpaces, .stationary]
        window.isMovableByWindowBackground = true

        // 画面右上に配置
        if let screen = NSScreen.main {
            let sx = screen.visibleFrame.maxX - 100
            let sy = screen.visibleFrame.maxY - 100
            window.setFrameOrigin(NSPoint(x: sx, y: sy))
        }

        window.makeKeyAndOrderFront(nil)
        NSApp.setActivationPolicy(.accessory)  // Dock に出ない
    }
}

// ─────────────────────────────────────────
// MARK: - Status Model
// ─────────────────────────────────────────

enum CompileStatus: String {
    case compiling
    case done
    case error
    case unknown

    var color: Color {
        switch self {
        case .compiling: return Color(red: 1.0, green: 0.18, blue: 0.18) // 赤
        case .done:      return Color(red: 0.18, green: 0.55, blue: 1.0) // 青
        case .error:     return Color(red: 1.0, green: 0.65, blue: 0.0)  // オレンジ
        case .unknown:   return Color(white: 0.4)
        }
    }

    var label: String {
        switch self {
        case .compiling: return "..."
        case .done:      return "OK"
        case .error:     return "ERR"
        case .unknown:   return "?"
        }
    }
}

// ─────────────────────────────────────────
// MARK: - ViewModel
// ─────────────────────────────────────────

class StatusWatcher: ObservableObject {
    static let watchedFile = "/Users/kenichinishi/unity/SnowPanicVibe/Assets/Logs/compile_status.json"

    @Published var status: CompileStatus = .unknown
    @Published var updatedAt: String = ""

    private var timer: Timer?
    private var lastRawStatus: String = ""

    init() {
        startPolling()
    }

    deinit {
        timer?.invalidate()
    }

    func startPolling() {
        timer = Timer.scheduledTimer(withTimeInterval: 0.8, repeats: true) { [weak self] _ in
            self?.poll()
        }
    }

    private func poll() {
        let url = URL(fileURLWithPath: StatusWatcher.watchedFile)
        guard let data = try? Data(contentsOf: url),
              let json = try? JSONSerialization.jsonObject(with: data) as? [String: String],
              let raw = json["status"] else {
            DispatchQueue.main.async { [weak self] in
                self?.status = .unknown
            }
            return
        }

        let at = json["updatedAt"] ?? ""
        let newStatus = CompileStatus(rawValue: raw) ?? .unknown

        DispatchQueue.main.async { [weak self] in
            guard let self else { return }
            let didChange = raw != self.lastRawStatus
            self.lastRawStatus = raw
            self.status = newStatus
            self.updatedAt = at

            // done に変わった瞬間だけ通知
            if didChange && newStatus == .done {
                self.sendNotification(title: "コンパイル完了", body: at)
            }
        }
    }

    private func sendNotification(title: String, body: String) {
        let content = UNMutableNotificationContent()
        content.title = title
        content.body = body
        content.sound = .default
        let req = UNNotificationRequest(identifier: UUID().uuidString,
                                        content: content, trigger: nil)
        UNUserNotificationCenter.current().add(req, withCompletionHandler: nil)
    }
}

// ─────────────────────────────────────────
// MARK: - View
// ─────────────────────────────────────────

struct MonitorView: View {
    @StateObject private var watcher = StatusWatcher()

    var body: some View {
        ZStack {
            // 背景: 半透明の丸角パネル
            RoundedRectangle(cornerRadius: 16)
                .fill(Color.black.opacity(0.55))

            VStack(spacing: 4) {
                // メインライト
                ZStack {
                    // グロー（外側の光）
                    Circle()
                        .fill(watcher.status.color.opacity(0.35))
                        .frame(width: 52, height: 52)
                        .blur(radius: 6)

                    // 本体
                    Circle()
                        .fill(watcher.status.color)
                        .frame(width: 36, height: 36)
                        .overlay(
                            // ハイライト（上部の白い反射）
                            Ellipse()
                                .fill(Color.white.opacity(0.30))
                                .frame(width: 16, height: 10)
                                .offset(x: -2, y: -8)
                        )
                        .shadow(color: watcher.status.color.opacity(0.8), radius: 8)
                }
                .animation(.easeInOut(duration: 0.3), value: watcher.status)

                // ラベル
                Text(watcher.status.label)
                    .font(.system(size: 9, weight: .bold, design: .monospaced))
                    .foregroundColor(.white.opacity(0.85))
            }
        }
        .frame(width: 72, height: 72)
    }
}

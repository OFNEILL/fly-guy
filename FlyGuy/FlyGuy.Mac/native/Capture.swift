import Foundation
import CoreGraphics
import ScreenCaptureKit

@_cdecl("fg_permission")
public func permission(_ request: Int32) -> Int32 {
    if CGPreflightScreenCaptureAccess() { return 1 }
    return request != 0 && CGRequestScreenCaptureAccess() ? 1 : 0
}

@_cdecl("fg_display")
public func display(_ index: Int32, _ x: UnsafeMutablePointer<Double>,
                    _ y: UnsafeMutablePointer<Double>, _ width: UnsafeMutablePointer<Double>,
                    _ height: UnsafeMutablePointer<Double>) -> Int32 {
    var count: UInt32 = 0
    guard CGGetActiveDisplayList(0, nil, &count) == .success else { return 0 }
    var displays = [CGDirectDisplayID](repeating: 0, count: Int(count))
    guard CGGetActiveDisplayList(count, &displays, &count) == .success,
          index >= 0, index < Int32(count) else { return 0 }
    let bounds = CGDisplayBounds(displays[Int(index)])
    x.pointee = bounds.minX; y.pointee = bounds.minY
    width.pointee = bounds.width; height.pointee = bounds.height
    return 1
}

@_cdecl("fg_cursor")
public func cursor(_ x: UnsafeMutablePointer<Double>, _ y: UnsafeMutablePointer<Double>) {
    let point = CGEvent(source: nil)?.location ?? .zero
    x.pointee = point.x; y.pointee = point.y
}

// Called only on the managed capture worker, never the AppKit/UI thread.
// Wait for completion before returning: the managed output buffer must stay pinned.
@_cdecl("fg_capture")
public func capture(_ x: Int32, _ y: Int32, _ size: Int32,
                    _ output: UnsafeMutablePointer<UInt8>) -> Int32 {
    guard CGPreflightScreenCaptureAccess() else { return -2 }
    guard size > 0 && size <= 2048 else { return -1 }
    let finished = DispatchSemaphore(value: 0)
    var result: Int32 = -1
    Task {
        defer { finished.signal() }
        do {
            let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
            let ownApps = content.applications.filter { $0.processID == ProcessInfo.processInfo.processIdentifier }
            // Do not silently feed the fly its own overlay if enumeration omits us.
            guard !ownApps.isEmpty else { return }
            let requested = CGRect(x: Int(x), y: Int(y), width: Int(size), height: Int(size))
            output.initialize(repeating: 0, count: Int(size * size * 4))
            var captured = false
            for screen in content.displays {
                let bounds = CGDisplayBounds(screen.displayID)
                let crop = bounds.intersection(requested)
                if crop.isNull || crop.isEmpty { continue }
                let filter = SCContentFilter(display: screen, excludingApplications: ownApps, exceptingWindows: [])
                let config = SCStreamConfiguration()
                config.sourceRect = crop.offsetBy(dx: -bounds.minX, dy: -bounds.minY)
                config.width = Int(crop.width); config.height = Int(crop.height)
                config.showsCursor = true
                config.scalesToFit = true
                let image = try await SCScreenshotManager.captureImage(contentFilter: filter, configuration: config)
                let width = Int(crop.width), height = Int(crop.height)
                var pixels = [UInt8](repeating: 0, count: width * height * 4)
                let rendered = pixels.withUnsafeMutableBytes { bytes -> Bool in
                    guard let context = CGContext(data: bytes.baseAddress, width: width, height: height,
                        bitsPerComponent: 8, bytesPerRow: width * 4, space: CGColorSpaceCreateDeviceRGB(),
                        bitmapInfo: CGImageAlphaInfo.premultipliedFirst.rawValue | CGBitmapInfo.byteOrder32Little.rawValue)
                        else { return false }
                    context.draw(image, in: CGRect(x: 0, y: 0, width: width, height: height))
                    return true
                }
                guard rendered else { return }
                let offsetX = Int(crop.minX - requested.minX), offsetY = Int(crop.minY - requested.minY)
                pixels.withUnsafeBufferPointer { source in
                    for row in 0..<height {
                        output.advanced(by: ((offsetY + row) * Int(size) + offsetX) * 4)
                            .update(from: source.baseAddress!.advanced(by: row * width * 4), count: width * 4)
                    }
                }
                captured = true
            }
            result = captured ? 1 : -1
        } catch { result = -1 }
    }
    finished.wait()
    return result
}

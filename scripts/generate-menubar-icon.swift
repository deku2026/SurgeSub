// Rebuild the monochrome macOS template icon: swift scripts/generate-menubar-icon.swift
import AppKit
import Foundation

let size = 64
let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: size, pixelsHigh: size,
                             bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true,
                             isPlanar: false, colorSpaceName: .deviceRGB,
                             bytesPerRow: 0, bitsPerPixel: 0)!
NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: bitmap)
NSColor.black.setStroke()
let path = NSBezierPath()
path.lineWidth = 6
path.lineCapStyle = .round
path.lineJoinStyle = .round
path.move(to: NSPoint(x: 11, y: 44)); path.line(to: NSPoint(x: 53, y: 44))
path.move(to: NSPoint(x: 43, y: 54)); path.line(to: NSPoint(x: 53, y: 44)); path.line(to: NSPoint(x: 43, y: 34))
path.move(to: NSPoint(x: 53, y: 20)); path.line(to: NSPoint(x: 11, y: 20))
path.move(to: NSPoint(x: 21, y: 30)); path.line(to: NSPoint(x: 11, y: 20)); path.line(to: NSPoint(x: 21, y: 10))
path.stroke()
NSGraphicsContext.restoreGraphicsState()
let output = URL(fileURLWithPath: "src/SurgeSub.App/Assets/MenuBar.png")
try FileManager.default.createDirectory(at: output.deletingLastPathComponent(), withIntermediateDirectories: true)
try bitmap.representation(using: .png, properties: [:])!.write(to: output)

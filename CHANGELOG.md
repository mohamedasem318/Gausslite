# Changelog

All notable changes to WAshed will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- BlurPipeline module: applies configurable Gaussian blur to captured frames using Win2D, with a default radius of 20 DIPs; render target is reused across frames and reallocated only when frame dimensions change
- CaptureEngine module: wraps Windows.Graphics.Capture to deliver per-frame textures via a free-threaded FrameArrived event; all WinRT factory calls are behind ICaptureInterop for unit-test isolation
- WindowTracker module: tracks WhatsApp Desktop window bounds at 10 Hz with per-monitor DPI awareness
- Project scaffolding and planning documents
#!/usr/bin/env bash
set -euo pipefail
task_root="$(cd "$(dirname "$0")/.." && pwd)"
task_apk="$task_root/artifacts/releases/android-x64/ApiNet-android-x64.apk"
task_results="$task_root/artifacts/android-smoke"
mkdir -p "$task_results"
adb install -r "$task_apk"
adb logcat -c
adb shell monkey -p io.github.coolqingcheng.apinet -c android.intent.category.LAUNCHER 1
sleep 12
adb logcat -d > "$task_results/logcat.txt"
if ! rg 'ApiNet.*Workspace initialized: [1-9][0-9]* requests' "$task_results/logcat.txt"; then
  tail -n 160 "$task_results/logcat.txt"
  exit 1
fi
task_pid="$(adb shell pidof io.github.coolqingcheng.apinet | tr -d '\r')"
if [[ -z "$task_pid" ]]; then
  tail -n 160 "$task_results/logcat.txt"
  exit 1
fi
adb shell screencap -p /sdcard/apinet-phone.png
adb pull /sdcard/apinet-phone.png "$task_results/android-phone.png"
adb shell settings put system accelerometer_rotation 0
adb shell settings put system user_rotation 1
sleep 3
adb shell screencap -p /sdcard/apinet-landscape.png
adb pull /sdcard/apinet-landscape.png "$task_results/android-landscape.png"
if rg 'FATAL EXCEPTION|FATAL UNHANDLED EXCEPTION|Unhandled Exception:' "$task_results/logcat.txt"; then exit 1; fi
echo 'Android APK installed and remained running in portrait and landscape.'

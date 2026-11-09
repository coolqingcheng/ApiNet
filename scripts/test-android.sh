#!/usr/bin/env bash
set -euo pipefail
task_root="$(cd "$(dirname "$0")/.." && pwd)"
task_apk="$task_root/artifacts/releases/android-x64/ApiNet-android-x64.apk"
task_results="$task_root/artifacts/android-smoke"
mkdir -p "$task_results"
adb install -r "$task_apk"
adb logcat -c
adb shell monkey -p io.github.coolqingcheng.apinet -c android.intent.category.LAUNCHER 1
for task_attempt in {1..15}; do
  sleep 2
  adb logcat -d > "$task_results/logcat.txt"
  if grep -Eq 'ApiNet.*Workspace initialized: [1-9][0-9]* requests' "$task_results/logcat.txt"; then break; fi
done
# The stock launcher can show an ANR while a fresh CI emulator indexes packages.
# Close only the emulator launcher so it cannot obscure the application's screenshots.
adb shell am force-stop com.android.launcher3
sleep 3
adb shell screencap -p /sdcard/apinet-phone.png
adb pull /sdcard/apinet-phone.png "$task_results/android-phone.png"
adb shell settings put system accelerometer_rotation 0
adb shell settings put system user_rotation 1
sleep 3
adb shell screencap -p /sdcard/apinet-landscape.png
adb pull /sdcard/apinet-landscape.png "$task_results/android-landscape.png"
adb logcat -d > "$task_results/logcat.txt"
adb shell dumpsys window > "$task_results/windows.txt"
if ! grep -E 'mCurrentFocus=.*io.github.coolqingcheng.apinet/' "$task_results/windows.txt"; then
  tail -n 120 "$task_results/windows.txt"
  exit 1
fi
if ! grep -E 'ApiNet.*Workspace initialized: [1-9][0-9]* requests' "$task_results/logcat.txt"; then
  tail -n 160 "$task_results/logcat.txt"
  exit 1
fi
task_pid="$(adb shell pidof io.github.coolqingcheng.apinet | tr -d '\r')"
if [[ -z "$task_pid" ]]; then
  tail -n 160 "$task_results/logcat.txt"
  exit 1
fi
if grep -E 'FATAL EXCEPTION|FATAL UNHANDLED EXCEPTION|Unhandled Exception:' "$task_results/logcat.txt"; then exit 1; fi
echo 'Android APK installed and remained running in portrait and landscape.'

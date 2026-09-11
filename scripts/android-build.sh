#!/bin/bash
set -euo pipefail
project_root="$(cd "$(dirname "$0")/.." && pwd)"
export ANDROID_HOME="${ANDROID_HOME:-$HOME/Library/Android/sdk}"
if [[ -d "$project_root/.build/android-tools/jbr21/Contents/Home" ]]; then
    export JAVA_HOME="$project_root/.build/android-tools/jbr21/Contents/Home"
fi
if [[ ! -f "$ANDROID_HOME/platforms/android-36/android.jar" ]]; then
    echo 'Android SDK Platform 36 未完整安装，请在 SDK Manager 安装。' >&2
    exit 1
fi
cd "$project_root/android"
gradle_cmd=(./gradlew)
if [[ -x "$project_root/.build/android-tools/gradle-8.11.1/bin/gradle" ]]; then
    gradle_cmd=("$project_root/.build/android-tools/gradle-8.11.1/bin/gradle")
fi
proxy_args=()
if [[ -n "${ANDROID_BUILD_PROXY_HOST:-}" ]]; then
    proxy_args+=("-Dhttps.proxyHost=$ANDROID_BUILD_PROXY_HOST" "-Dhttps.proxyPort=${ANDROID_BUILD_PROXY_PORT:-7890}" "-Dhttp.proxyHost=$ANDROID_BUILD_PROXY_HOST" "-Dhttp.proxyPort=${ANDROID_BUILD_PROXY_PORT:-7890}")
fi
if [[ $# -eq 0 ]]; then set -- :app:assembleRelease :app:testDebugUnitTest; fi
if [[ ${#proxy_args[@]} -gt 0 ]]; then
    "${gradle_cmd[@]}" "$@" "${proxy_args[@]}" --console=plain
else
    "${gradle_cmd[@]}" "$@" --console=plain
fi

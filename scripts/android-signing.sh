#!/bin/bash
# Create a private, persistent signing identity for locally distributed APKs.
# This directory is ignored by git and must be backed up to retain update compatibility.
set -euo pipefail
project_root="$(cd "$(dirname "$0")/.." && pwd)"
signing_dir="$project_root/android/signing"
if [[ -d "$project_root/.build/android-tools/jbr21/Contents/Home" ]]; then
    export JAVA_HOME="$project_root/.build/android-tools/jbr21/Contents/Home"
fi
keytool_cmd="${JAVA_HOME:+$JAVA_HOME/bin/}keytool"
if [[ -f "$signing_dir/mandu-release.p12" && -f "$signing_dir/password.txt" ]]; then
    echo '已有本地发布签名，保持不变。'
    exit 0
fi
if [[ -e "$signing_dir/mandu-release.p12" || -e "$signing_dir/password.txt" ]]; then
    echo '签名资料不完整；请恢复原密钥和密码，不会自动覆盖。' >&2
    exit 1
fi
umask 077
mkdir -p "$signing_dir"
openssl rand -hex -out "$signing_dir/password.txt" 32
"$keytool_cmd" -genkeypair -keystore "$signing_dir/mandu-release.p12" \
    -storetype PKCS12 -storepass:file "$signing_dir/password.txt" \
    -keypass:file "$signing_dir/password.txt" -alias mandu -keyalg RSA -keysize 3072 \
    -validity 10000 -dname 'CN=Mandu Android, O=Local Distribution' -noprompt
echo '本地发布签名已生成。请私下备份 android/signing，勿上传或提交。'

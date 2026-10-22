#!/bin/sh
set -eu

source_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P)
data_dir=${XDG_DATA_HOME:-"$HOME/.local/share"}
install_dir="$data_dir/filee/application"
bin_dir="$HOME/.local/bin"
desktop_file="$data_dir/applications/com.filee.app.desktop"

# Close running Filee instances
pkill -f "Filee" 2>/dev/null || true

mkdir -p "$data_dir/filee" "$bin_dir" "$data_dir/applications"
rm -rf "$install_dir"
cp -R "$source_dir" "$install_dir"
printf '%s\n' 'com.filee.app' > "$install_dir/.filee-package"
rm -f "$bin_dir/filee"
ln -s "$install_dir/filee-cli" "$bin_dir/filee"

python3 - "$install_dir" "$desktop_file" <<'PY'
import pathlib
import sys

app, desktop = map(pathlib.Path, sys.argv[1:])
def quoted_exec(value):
    value = value.replace('%', '%%')
    for character in ('\\', '"', '`', '$'):
        value = value.replace(character, '\\' + character)
    return '"' + value.replace('\\', '\\\\').replace('\n', '\\n').replace('\r', '\\r') + '"'

def desktop_value(value):
    return str(value).replace('\\', '\\\\').replace('\n', '\\n').replace('\r', '\\r')

desktop.write_text(
    '[Desktop Entry]\nType=Application\nName=Filee\n'
    'Comment=Convert files with Filee (Native Wayland)\n'
    f'Exec={quoted_exec(str(app / "Filee"))} --convert %F\n'
    f'Icon={desktop_value(app / "filee.png")}\n'
    'Terminal=false\nCategories=Utility;\nX-Filee-Managed=true\n', encoding='utf-8')
PY

if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database "$data_dir/applications"
fi

printf '%s\n' "Installed Filee (Native Wayland) at $install_dir"

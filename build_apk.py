import os
import subprocess
import zipfile
import shutil

ROOT_DIR = os.path.dirname(os.path.abspath(__file__))
APP_DIR = os.path.join(ROOT_DIR, "android_app")
TOOLS_DIR = os.path.join(ROOT_DIR, "tools")
BUILD_DIR = os.path.join(APP_DIR, "build")
DIST_DIR = os.path.join(ROOT_DIR, "dist")

AAPT2 = os.path.join(TOOLS_DIR, "aapt2.exe")
R8_JAR = os.path.join(TOOLS_DIR, "r8.jar")
ANDROID_JAR = os.path.join(TOOLS_DIR, "android.jar")
KEYSTORE = os.path.join(TOOLS_DIR, "debug.keystore")

def find_jdk_binary(name):
    check = shutil.which(name)
    if check: return check
    candidates = [
        rf"C:\Program Files\Microsoft\jdk-17.0.20.101-hotspot\bin\{name}.exe",
        rf"C:\Program Files\Java\jdk-17\bin\{name}.exe",
        rf"C:\Program Files\Eclipse Adoptium\jdk-17\bin\{name}.exe"
    ]
    for c in candidates:
        if os.path.exists(c): return c
    return f"{name}.exe"

JAVAC = find_jdk_binary("javac")
JARSIGNER = find_jdk_binary("jarsigner")

def run_cmd(cmd, desc):
    print(f"[*] {desc}...")
    res = subprocess.run(cmd, shell=True, capture_output=True, text=True)
    if res.returncode != 0:
        print(f"[!] FAILED: {desc}")
        print("STDOUT:", res.stdout)
        print("STDERR:", res.stderr)
        raise RuntimeError(f"Step failed: {desc}")
    print(f"[+] {desc} complete.")
    return res.stdout

def build():
    os.makedirs(BUILD_DIR, exist_ok=True)
    os.makedirs(DIST_DIR, exist_ok=True)
    
    compiled_res = os.path.join(BUILD_DIR, "compiled_res.zip")
    gen_dir = os.path.join(BUILD_DIR, "gen")
    bin_dir = os.path.join(BUILD_DIR, "bin")
    dex_dir = os.path.join(BUILD_DIR, "dex")
    os.makedirs(gen_dir, exist_ok=True)
    os.makedirs(bin_dir, exist_ok=True)
    os.makedirs(dex_dir, exist_ok=True)

    # 1. Compile resources with aapt2
    res_dir = os.path.join(APP_DIR, "res")
    cmd_compile = f'"{AAPT2}" compile --dir "{res_dir}" -o "{compiled_res}"'
    run_cmd(cmd_compile, "AAPT2 Compile Resources")

    # 2. Link resources with aapt2
    manifest = os.path.join(APP_DIR, "AndroidManifest.xml")
    assets_dir = os.path.join(APP_DIR, "assets")
    unaligned_apk = os.path.join(BUILD_DIR, "unaligned.apk")
    cmd_link = (
        f'"{AAPT2}" link -I "{ANDROID_JAR}" --manifest "{manifest}" '
        f'"{compiled_res}" -A "{assets_dir}" -o "{unaligned_apk}" --java "{gen_dir}"'
    )
    run_cmd(cmd_link, "AAPT2 Link APK & Generate R.java")

    # 3. Compile Java with javac
    java_files = []
    for root, _, files in os.walk(gen_dir):
        for f in files:
            if f.endswith(".java"):
                java_files.append(os.path.join(root, f))
    for root, _, files in os.walk(os.path.join(APP_DIR, "src")):
        for f in files:
            if f.endswith(".java"):
                java_files.append(os.path.join(root, f))

    sources_file = os.path.join(BUILD_DIR, "sources.txt")
    with open(sources_file, "w", encoding="utf-8") as f:
        for jf in java_files:
            f.write(jf.replace("\\", "/") + "\n")

    cmd_javac = f'"{JAVAC}" -encoding UTF-8 -cp "{ANDROID_JAR}" -d "{bin_dir}" --release 8 @"sources.txt"'
    run_cmd(f'cd /d "{BUILD_DIR}" && {cmd_javac}', "Compile Java Sources (Java 8 bytecode)")

    # 4. Convert classes to DEX with D8
    class_files = []
    for root, _, files in os.walk(bin_dir):
        for f in files:
            if f.endswith(".class"):
                class_files.append(os.path.join(root, f).replace("\\", "/"))

    classes_file = os.path.join(BUILD_DIR, "classes.txt")
    with open(classes_file, "w", encoding="utf-8") as f:
        for cf in class_files:
            f.write(cf + "\n")

    cmd_d8 = (
        f'java -cp "{R8_JAR}" com.android.tools.r8.D8 '
        f'--lib "{ANDROID_JAR}" --output "{dex_dir}" @"classes.txt"'
    )
    run_cmd(f'cd /d "{BUILD_DIR}" && {cmd_d8}', "Convert to Dalvik Executable (classes.dex)")

    # 5. Add classes.dex to APK
    classes_dex = os.path.join(dex_dir, "classes.dex")
    target_apk = os.path.join(DIST_DIR, "ClaudeBarrier-Android.apk")
    shutil.copyfile(unaligned_apk, target_apk)

    with zipfile.ZipFile(target_apk, "a") as z:
        z.write(classes_dex, "classes.dex")
    print("[+] Added classes.dex to APK.")

    # 6. Sign APK with jarsigner
    cmd_sign = (
        f'"{JARSIGNER}" -verbose -sigalg SHA256withRSA -digestalg SHA-256 '
        f'-keystore "{KEYSTORE}" -storepass android "{target_apk}" androiddebugkey'
    )
    run_cmd(cmd_sign, "Sign ClaudeBarrier-Android.apk")
    shutil.copyfile(target_apk, os.path.join(DIST_DIR, "ProjectXO.apk"))

    print("\n" + "="*60)
    print(f"[+] SUCCESS! ProjectXO.apk created at:\n    {target_apk}")
    print(f"    Size: {os.path.getsize(target_apk)} bytes")
    print("="*60)

if __name__ == "__main__":
    build()

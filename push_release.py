import subprocess
import os

git_exe = r"C:\Users\bbara\AppData\Local\GitHubDesktop\app-3.5.8\resources\app\git\cmd\git.exe"
cwd = r"C:\Users\bbara\.gemini\antigravity\scratch\project_xo"

def run(args):
    print("[*] Running:", " ".join(args))
    res = subprocess.run([git_exe] + args, cwd=cwd, capture_output=True, text=True, encoding="utf-8")
    if res.stdout:
        print("STDOUT:", res.stdout)
    if res.stderr:
        print("STDERR:", res.stderr)
    if res.returncode != 0:
        raise RuntimeError(f"Git command failed with code {res.returncode}")

run(["add", "-A"])
run(["commit", "-m", "Release v5.0.0: Production Claude Anti-Ban Barrier // Bogdan Zhukov"])
run(["tag", "-f", "-a", "v5.0.0", "-m", "Claude Anti-Ban Barrier v5.0.0"])
run(["push", "origin", "main"])
run(["push", "origin", "v5.0.0", "--force"])
print("[+] Git push completed successfully!")

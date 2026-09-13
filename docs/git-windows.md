# Windows 上提交代码

Git 的 `commit` 把改动保存到本地历史，`push` 才会把这些提交上传到 GitHub。下面的命令在 PowerShell 中运行。

## 日常修改

先进入项目并更新当前分支。工作区已有未提交改动时，先保存并提交这些改动，再拉取更新。

```powershell
cd D:\shuiman\ShuiMan-
git status
git pull --ff-only
```

修改文件后，检查具体差异，把本次需要提交的文件加入暂存区。例如只修改了 README：

```powershell
git diff
git add README.md
git diff --cached
git commit -m "更新 Windows 使用说明"
git push
```

修改多个文件时，在 `git add` 后列出它们的路径。`git diff --cached` 显示即将提交的内容；确认没有个人漫画、密码、令牌、书库记录或构建产物。仓库的 `.gitignore` 已排除常见私人数据和构建目录。

代码改动提交前运行对应检查。Windows 版本使用：

```powershell
.\scripts\windows-test.ps1
.\scripts\windows-ui-test.ps1
```

## 较大功能使用分支

从最新主分支创建一个功能分支：

```powershell
git switch main
git pull --ff-only
git switch -c feature/my-change
```

完成改动、检查和提交后，首次上传该分支：

```powershell
git push -u origin feature/my-change
```

随后在 GitHub 项目页面创建 Pull Request，检查通过后合并到 `main`。`-u` 记住远端对应分支，此后只需 `git push`。如果 `git pull --ff-only` 或 `git push` 提示历史冲突，先查看提示并处理双方改动，不要直接强制推送。

## 发布可下载的程序

```powershell
.\scripts\windows-build.ps1
```

程序包和校验文件生成在 `build/windows/`。代码通过 Git 提交；`ShuiMan-Windows-x64.zip` 和 `.sha256` 通过 GitHub 的 Releases 页面作为版本附件上传。版本标签应对应已测试的代码提交，更新说明写清功能、安装方式和已知限制。无需把几十 MB 的程序包加入 Git 历史。

本次 Windows 发布的更新说明位于 [windows-v0.6.0.md](releases/windows-v0.6.0.md)，日后可参照它编写新版本说明。

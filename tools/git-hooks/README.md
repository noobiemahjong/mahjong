# git-hooks

`commit-msg` 钩子：本地提交时校验提交信息，规则与 [docs/development.md](../../docs/development.md) 的提交约定一致。

## 安装

```sh
git config core.hooksPath tools/git-hooks
```

设置只作用于当前克隆，不入库；CI 在跑钩子测试前设置同一项。

## 规则

- 首行匹配 `^(feat|fix|docs|refactor|chore|test|perf|build|ci)(\(.+\))?: .{1,50}$`。
- 允许的 type：`feat` `fix` `docs` `refactor` `chore` `test` `perf` `build` `ci`，可带 `(scope)`。
- 标题正文（`: ` 之后的部分）1–50 字符：按字符数判定，不按字节，中文标题不会被误杀。
- 正文（首行之后）非空行 ≤10，空行不计。
- 消息为空、消息文件不存在或不可读：exit 1。
- 违规时把原因写到 stderr 并 exit 1；通过时静默 exit 0。

## 判定细节

- 字符数用字节分类统计：UTF-8 中 `<0x80` 或 `>=0xC0` 的字节数即字符数，不依赖 `LC_ALL`；`wc -m` 在 C locale 下按字节计，不能用。
- 匹配与行解析在 `LC_ALL=C` 下进行；正文行先去掉 `CR` 再计数。
- 钩子源码纯 ASCII（门禁 G9）。

## 测试

```pwsh
pwsh -NoProfile -File tools/git-hooks/tests/run.ps1
```

脚本在系统临时目录建两个一次性 git 仓库（`core.hooksPath` 指向本目录），跑好消息与坏消息用例，断言退出码与 HEAD 前进；全过 exit 0，任一失败 exit 1，结束时清理临时目录。用例矩阵见脚本内的用例表；CI 的 `.github/workflows/ci.yml` 在文档门禁之后跑同一条命令。

`run.ps1` 另做直连自检：把同一批消息文件直接喂给 `sh <hook> <file>`，退出码必须与 git 调用一致；找不到 `sh` 时打印 SKIP 并继续。

## 绕过

`git commit --no-verify` 跳过全部钩子，仅限紧急修复；事后补一次合规提交说明原因，不要用它提交功能改动。

## 跨平台

- Git for Windows 用自带 `sh` 执行 `#!/bin/sh` 钩子；Windows 不追踪可执行位，Linux/macOS 检出后按需执行 `git update-index --chmod=+x tools/git-hooks/commit-msg`。
- `.gitattributes` 对本目录声明 `text eol=lf`，避免 CRLF 让钩子失效。

# WinPilot

원격 PC 상태 확인 도구 (WPF)

## 빠른 실행 (설치 불필요)

원격 PC에서 PowerShell을 열고 아래 명령어를 붙여넣으세요:

```powershell
$p="$env:TEMP\WinPilot.exe"; irm https://github.com/YOUR_USERNAME/YOUR_REPO/releases/latest/download/WinPilot.exe -OutFile $p; Start-Process $p
```

> `YOUR_USERNAME/YOUR_REPO`를 실제 GitHub 경로로 변경하세요.

## 릴리즈 방법

태그를 push하면 GitHub Actions가 자동으로 빌드 후 릴리즈에 업로드합니다:

```powershell
git tag v1.0.0
git push origin v1.0.0
```

## 개발 환경

- .NET 9 / WPF
- CommunityToolkit.Mvvm

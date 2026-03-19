# MailDevMcp

[English](README.md) | 日本語

[![CI](https://github.com/pierre3/MailDevMcp/actions/workflows/ci.yml/badge.svg)](https://github.com/pierre3/MailDevMcp/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/MailDevMcp.svg)](https://www.nuget.org/packages/MailDevMcp)

[MailDev](https://github.com/maildev/maildev) 向けの [MCP (Model Context Protocol)](https://modelcontextprotocol.io/) サーバーです。  
AI 対応エディターや MCP クライアントから、MailDev Docker コンテナーの操作や受信メールの確認を直接行えます。

このサーバーは、MCP クライアントから次のようなローカル開発・テスト自動化を行う用途を想定しています。

- MailDev コンテナーの起動・停止
- 受信トレイの確認
- アプリケーション動作後のメール到着待機
- HTML 本文や添付ファイルの確認
- 添付ファイルの完全一致検証

## インストール

グローバル .NET ツールとしてインストールします。

```sh
dotnet tool install -g MailDevMcp
```

インストール後、`maildev-mcp` コマンドが利用可能になります。

## MCP クライアント設定

Claude Desktop、VS Code などの MCP クライアント設定に、次の内容を追加してください。

```json
{
  "mcpServers": {
    "maildev-mcp": {
      "command": "maildev-mcp",
      "env": {
        "MAILDEV_API_PORT": "1080"
      }
    }
  }
}
```

### 環境変数

| 変数 | 既定値 | 説明 |
|---|---|---|
| `MAILDEV_API_PORT` | `1080` | MailDev REST API のポート番号 |

## 典型的な利用フロー

1. `StartMaildev` を呼び出してローカルの MailDev コンテナーを起動する
2. アプリケーションからメール送信を発生させる
3. 非同期で届く場合は `WaitForEmail` を呼び出す
4. `ListEmails`、`SearchEmails`、`GetEmail` で結果を確認する
5. より詳細な検証が必要な場合は `GetEmailHtml`、`GetAttachmentContent`、`VerifyAttachment` を使う
6. `DeleteEmail`、`DeleteAllEmails`、`StopMaildev` で後片付けする

## 利用可能なツール

| ツール | 説明 |
|---|---|
| `StartMaildev` | SMTP ポート、API ポート、認証、TLS を指定して MailDev Docker コンテナーを起動します |
| `StopMaildev` | MailDev Docker コンテナーを停止・削除します |
| `MaildevStatus` | MailDev の起動状態を確認します |
| `ListEmails` | 受信したメールの一覧を取得します |
| `GetEmail` | メール ID を指定して、添付ファイル情報を含む詳細を取得します |
| `GetEmailHtml` | メール ID を指定して HTML 本文を取得します |
| `SearchEmails` | 件名、送信者、受信者で受信メールを検索します |
| `DeleteEmail` | ID を指定して 1 件のメールを削除します |
| `DeleteAllEmails` | 受信メールをすべて削除します |
| `WaitForEmail` | 条件に一致するメールが届くまで待機します（テスト自動化向け） |
| `GetAttachmentContent` | 添付ファイルの Base64 エンコード済み内容を取得します |
| `VerifyAttachment` | 添付ファイルが元データとバイト単位で一致するか検証します |

## ツール挙動メモ

- `StartMaildev`
  - SMTP / API のカスタムポートに対応しています
  - SMTP 認証を任意で有効化できます
  - 空文字または空白のみの認証情報は「認証無効」として扱います
  - 自己署名証明書を用いた TLS に対応しています
- `GetEmail` / `ListEmails`
  - 件名が未設定または空の場合は `(no subject)` と表示します
  - アドレス一覧が存在しない場合は `(none)` と表示します
- `WaitForEmail`
  - 条件に一致するメールが届くまで、またはタイムアウトまで MailDev をポーリングします
  - ポーリング中に一時的な MailDev 接続失敗が発生した場合、その回数を返却メッセージに含めます
- `VerifyAttachment`
  - 元データは Base64 文字列で受け取ります
  - 受信した添付ファイルをバイト単位で比較します
  - 完全一致、サイズ不一致、内容不一致を判定して返します

## 前提条件と注意事項

- Docker は、この MCP サーバーを実行するマシン上で利用可能である必要があります
- MailDev には `localhost` 上の HTTP API 経由でアクセスします
- MCP クライアントで設定する API ポートは、Docker で公開する MailDev API ポートと一致している必要があります

## 前提ソフトウェア

- [.NET 10 SDK](https://dotnet.microsoft.com/) 以降
- [Docker](https://www.docker.com/)（MailDev コンテナー実行用）

## ソースからビルドする場合

```sh
git clone https://github.com/pierre3/MailDevMcp.git
cd MailDevMcp
dotnet pack -c Release
dotnet tool install -g --add-source ./bin/Release MailDevMcp
```

## ライセンス

[MIT](LICENSE)

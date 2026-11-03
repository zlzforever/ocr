# OCR Web API

一个基于 .NET 10 Minimal API Slim 和 SimdPaddleOCR 的轻量级中文 OCR 服务。

## 功能特性

- 使用 SimdPaddleOCR 中文 V6 Medium 模型（当前模型包中最大版本），模型随应用发布，不依赖运行时下载。
- 仅使用 CPU 推理；模型首次加载发生在第一次识别请求，首次请求耗时会明显增加。
- 使用 `Sdcb.SimdCodecs.Jpeg` 解码，仅接受原始 JPEG 请求体，不接受 PNG、multipart/form-data 或 Base64 图片。
- 单次请求最多 10 MB，图片最多 2,000 万像素。
- 本地开发使用普通 JIT 模式；Docker 镜像使用 Native AOT 和精简运行时镜像。

## 目录结构

```text
.
├── src/Ocr.Api/                 # Web API 和 OCR 推理代码
│   ├── Program.cs               # HTTP 端点、请求限制和 JSON 配置
│   └── OcrEngine.cs             # JPEG 解码、模型加载和推理
├── Dockerfile                   # linux/amd64 Native AOT 镜像
├── .github/workflows/docker.yml # Docker Hub 构建与推送流程
├── Ocr.sln                     # Visual Studio / dotnet 解决方案
└── README.md
```

## 环境要求

本地开发需要：

- .NET 10 SDK
- 支持 CPU 推理的 x64 运行环境

构建 Docker 镜像还需要 Docker。GitHub Actions 发布镜像需要 Docker Hub 仓库 `zlzforever/ocr` 的推送权限。

## 本地开发

### 还原和编译

```sh
dotnet restore Ocr.sln
dotnet build Ocr.sln
```

本地开发默认使用普通 JIT，不启用 Native AOT：

```sh
dotnet run --project src/Ocr.Api
```

开发启动配置监听 `http://localhost:5270`。也可以通过 `ASPNETCORE_URLS` 指定地址：

```sh
ASPNETCORE_URLS=http://localhost:5271 dotnet run \
  --project src/Ocr.Api \
  --no-launch-profile
```

### 健康检查

```sh
curl -i http://localhost:5270/health
```

成功响应为 HTTP 200，响应体为：

```text
ok
```

## HTTP API

### `POST /api/ocr`

识别一张 JPEG 图片中的文字。

| 项目 | 要求 |
| --- | --- |
| `Content-Type` | 必须为 `image/jpeg` |
| 请求体 | JPEG 原始二进制数据，不是 multipart |
| 请求体大小 | 1 字节至 10 MB |
| 图片尺寸 | 不超过 2,000 万像素 |
| 返回类型 | `application/json` |

请求示例：

```sh
curl --fail-with-body -sS \
  -X POST http://localhost:5270/api/ocr \
  -H 'Content-Type: image/jpeg' \
  --data-binary @sample.jpg
```

成功响应：

```json
{
  "text": "识别出的文字"
}
```

其中 `text` 按模型返回的阅读顺序排列；图片没有可识别文字时为空字符串。

常见错误：

| HTTP 状态码 | 场景 |
| --- | --- |
| `400 Bad Request` | `Content-Type` 不正确、空请求体、JPEG 无效或图片超过 2,000 万像素 |
| `413 Payload Too Large` | 请求体超过服务器的 10 MB 限制 |
| `500 Internal Server Error` | 服务端 OCR 推理失败 |

错误响应可能是纯文本。生产环境应记录服务端日志，并由调用方根据 HTTP 状态码处理失败请求。

### `GET /health`

用于容器编排或负载均衡器探活。该端点不加载 OCR 模型，只要 Web 服务正在运行就返回 `200 OK`。

## Docker

构建镜像：

```sh
docker build -t zlzforever/ocr:local .
```

启动容器：

```sh
docker run --rm --name ocr-api -p 8080:8080 zlzforever/ocr:local
```

容器内服务监听 `8080` 端口，验证方式：

```sh
curl -i http://localhost:8080/health
curl --fail-with-body -sS \
  -X POST http://localhost:8080/api/ocr \
  -H 'Content-Type: image/jpeg' \
  --data-binary @sample.jpg
```

当前 Dockerfile 的发布目标为 `linux/amd64`。镜像构建阶段启用 Native AOT、裁剪符号和尺寸优化，运行阶段使用无 shell 的 `runtime-deps` chiseled 镜像；本地 `dotnet run` 不受这些 Docker 发布参数影响。

## GitHub Actions 发布

`.github/workflows/docker.yml` 在以下情况下构建并推送镜像：

- `main` 分支收到 push；
- 手动执行 workflow（`workflow_dispatch`）。

需要在 GitHub 仓库的 Actions secrets 中配置：

| Secret | 用途 |
| --- | --- |
| `DOCKERHUB_USERNAME` | Docker Hub 用户名 |
| `DOCKERHUB_TOKEN` | Docker Hub Access Token |

工作流只构建 `linux/amd64`，并推送以下标签到 `zlzforever/ocr`：

- `latest`
- 当前 Git 提交 SHA

请确认 Docker Hub 账号对目标仓库具有推送权限。不要把 Docker Hub token 写入仓库文件或提交记录。

## 安全和生产使用注意事项

- 当前 API 没有内置认证和授权，不要直接暴露到公网；请在反向代理或 API 网关中配置认证、TLS、限流和访问控制。
- 服务只支持 HTTP 图片上传，不提供文件存储功能；调用方应自行校验来源和权限。
- OCR 推理使用共享模型并串行执行，适合轻量服务场景；高并发场景应通过多实例扩展，并结合网关限流。
- 模型首次加载可能较慢，部署后建议先调用一次 OCR 接口完成预热，并将 `/health` 用作存活检查。

## 许可证

本项目采用 [MIT License](LICENSE)。

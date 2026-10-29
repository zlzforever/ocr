# OCR Web API

基于 .NET 10 Minimal API Slim + SimdPaddleOCR 中文 V6 Tiny 模型。模型随程序发布，不依赖在线下载；仅支持 CPU 推理。首次识别会加载模型，耗时可能较长。

## 本地运行（普通 JIT）

```sh
dotnet run --project src/Ocr.Api
curl -X POST http://localhost:5270/api/ocr -H 'Content-Type: image/jpeg' --data-binary @sample.jpg
```

`POST /api/ocr` 接收原始 JPEG/PNG 图片字节（非 multipart），返回 `{"text":"..."}`；空图、无效图片或超过 2000 万像素返回 400；请求体最多 10 MB（超过限制返回 413）。`GET /health` 返回 200。未配置认证，请在网关前加访问控制，不要直接暴露公网。

## Docker / 发布

```sh
docker build -t zlzforever/ocr .
docker run --rm -p 8080:8080 zlzforever/ocr
```

镜像仅构建 `linux/amd64`（x64 CPU），使用 Native AOT 和无 shell 的 chiseled runtime-deps；本地构建不启用 AOT。GitHub Actions 在 `main` 推送和手动触发时推送 Docker Hub `zlzforever/ocr:latest` 及提交 SHA 标签。仓库须配置 `DOCKERHUB_USERNAME` 和 `DOCKERHUB_TOKEN` Secrets，且账号有对应仓库的推送权限。

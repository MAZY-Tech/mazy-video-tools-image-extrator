# mazy-video-tools-image-extractor

[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=MAZY-Tech_mazy-video-tools-image-extrator&metric=coverage)](https://sonarcloud.io/summary/new_code?id=MAZY-Tech_mazy-video-tools-image-extrator)
[![Lines of Code](https://sonarcloud.io/api/project_badges/measure?project=MAZY-Tech_mazy-video-tools-image-extrator&metric=ncloc)](https://sonarcloud.io/summary/new_code?id=MAZY-Tech_mazy-video-tools-image-extrator)
[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=MAZY-Tech_mazy-video-tools-image-extrator&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=MAZY-Tech_mazy-video-tools-image-extrator)

Este projeto oferece uma função AWS Lambda robusta e resiliente para processar mensagens SQS descrevendo vídeos. A função extrai frames em blocos, compacta em um arquivo ZIP e gerencia o estado da tarefa em um banco MongoDB.

---

## Visão Geral

A função `ImageExtractor` é acionada por mensagens em uma fila SQS. Para cada mensagem, ela executa:

1. **Validação e Inicialização**

   * Lê variáveis de ambiente (ex.: conexão MongoDB, buckets S3).
   * Valida a estrutura da mensagem recebida.

2. **Download do Vídeo**

   * Baixa o arquivo de vídeo de um bucket S3 de origem.

3. **Análise do Vídeo**

   * Usa `ffprobe` para obter duração total e contagem de frames.

4. **Extração de Frames em Blocos**

   * Segmenta o vídeo em blocos menores (ex.: 30s).
   * Usa `ffmpeg` para extrair frames de cada bloco.
   * Envia os frames para um bucket S3, organizados por ID de vídeo e bloco.
   * Atualiza o progresso no MongoDB e envia mensagens de atualização para outra fila SQS.
   * Implementa retomada automática em caso de falha, com base em `LastProcessedSecond` e `CurrentBlock`.

5. **Criação e Upload do ZIP**

   * Após a extração completa, compacta os frames em um .zip e faz upload no bucket S3 designado.

6. **Finalização**

   * Marca o job como `COMPLETED` no MongoDB.
   * Envia mensagem final de conclusão para a fila de progresso.

A função é idempotente, prevenindo reprocessamento desnecessário, e redireciona falhas para uma **Dead Letter Queue (DLQ)** do SQS.

---

## Tecnologias-Chave

* **AWS Lambda** – Execução serverless.
* **Amazon SQS** – Fila principal de entrada e fila de progresso.
* **Amazon S3** – Armazena vídeos, frames extraídos e arquivos ZIP.
* **MongoDB/DocumentDB** – Armazena o estado do processamento.
* **FFmpeg/FFprobe** – Ferramentas para análise e extração de frames.
* **.NET 8** – Plataforma de desenvolvimento.

---

## Pré-requisitos

1. [**.NET SDK 8**](https://dotnet.microsoft.com/download)
2. **AWS CLI**
3. **Amazon.Lambda.Tools** (global ou local)
4. **FFmpeg** e **FFprobe** para Linux (empacotados com a Lambda)
5. **Instância MongoDB/DocumentDB**
6. **Buckets S3** para entrada, frames e saída
7. **Filas SQS** (principal e de progresso)

---

## Variáveis de Ambiente

Configure as variáveis na função Lambda:

| Variável             | Descrição                                                  | Exemplo de Valor                                                   |
| -------------------- | ---------------------------------------------------------- | ------------------------------------------------------------------ |
| **Obrigatórias**     |                                                            |                                                                    |
| `SENTRY_DSN`         | DSN para relatório de erros no Sentry                      | `https://xxxxxxx@sentry.io/xxxxx`                                  |
| `FRAMES_BUCKET_NAME` | Nome do bucket S3 para armazenar os frames extraídos       | `my-video-frames-bucket`                                           |
| `ZIP_BUCKET_NAME`    | Bucket onde será enviado o arquivo ZIP final               | `my-video-zips-bucket`                                             |
| `PROGRESS_QUEUE_URL` | URL da fila SQS para mensagens de progresso                | `https://sqs.us-east-1.amazonaws.com/123456789012/MyProgressQueue` |
| `MONGO_DB_HOST`      | Host do cluster MongoDB/DocumentDB                         | `docdb-xxxx.cluster-abcdefghij.us-east-1.docdb.amazonaws.com`      |
| `MONGO_DB_USER`      | Usuário de autenticação MongoDB                            | `lambdauser`                                                       |
| `MONGO_DB_PASSWORD`  | Senha de autenticação MongoDB                              | `mySecurePassword123`                                              |
| `DATABASE_NAME`      | Nome do banco MongoDB/DocumentDB                           | `video_processing`                                                 |
| `COLLECTION_NAME`    | Nome da coleção MongoDB/DocumentDB                         | `jobs`                                                             |
| **Opcionais**        |                                                            |                                                                    |
| `FRAME_EXTENSION`    | Extensão dos arquivos de frame (default: `jpg`)            | `png`                                                              |
| `FRAME_RATE`         | Frames por segundo a extrair (default: `1`)                | `2`                                                                |
| `BLOCK_SIZE`         | Tamanho dos blocos de extração em segundos (default: `30`) | `60`                                                               |

> Configure valores de teste para essas variáveis ao rodar `dotnet test` se houver validações em tempo de execução.

---

## Uso e Formatos de Dados

### Mensagem de Entrada (SQS Principal)

```json
{
  "video_id": "a4708f6c-b38a-41c2-a799-24f0f1e5f8f7",
  "video_hash": "sha1:4f9c1a38b8ed3f57a89c3cb674c64f2d9f23ec93",
  "cognito_user_id": "a4681438-70b1-70cb-308f-cc40ea50066a",
  "bucket": "meu-bucket-de-videos",
  "key": "folder-x/video-original.mp4",
  "timestamp": "2025-06-19T19:45:50+00:00"
}
```

### Mensagens de Progresso (SQS de Progresso)

#### Durante o processamento

```json
{
  "video_id": "a4708f6c-b38a-41c2-a799-24f0f1e5f8f7",
  "status": "RUNNING",
  "progress": 20,
  "current_block": 2,
  "total_blocks": 10,
  "timestamp": "2025-06-19T19:46:37+00:00"
}
```

#### Ao concluir

```json
{
  "video_id": "a4708f6c-b38a-41c2-a799-24f0f1e5f8f7",
  "status": "COMPLETED",
  "progress": 100,
  "timestamp": "2025-06-19T19:48:05+00:00",
  "zip": {
    "bucket": "my-video-zips-bucket",
    "key": "a4708f6c-b38a-41c2-a799-24f0f1e5f8f7.zip"
  }
}
```

### Documento no MongoDB (Coleção `jobs`)

```json
{
  "_id": "a4708f6c-b38a-41c2-a799-24f0f1e5f8f7",
  "status": "RUNNING",
  "progress": 20,
  "total_frames": 300,
  "processed_frames": 60,
  "last_processed_second": 60,
  "current_block": 2,
  "total_blocks": 10,
  "last_heartbeat": "2025-06-19T19:46:37.000Z",
  "current_step": "Extracting",
  "started_at": "2025-06-19T19:40:00.000Z",
  "completed_at": null
}
```

---

## Execução e Testes

### Terminal

Execute no diretório raiz:

```bash
dotnet test ImageExtractor.Tests
```

Executa todos os testes xUnit, com suporte a mocks e validações de ambiente.

### Visual Studio 2022

Abra a solução e utilize o **Test Explorer** para rodar e verificar os testes.

### Estrutura de Testes

1. **Testes de Unidade**

   * `ImageExtractionWorkflowTests.cs`: cobre as etapas principais (download, análise, extração, compactação).
   * `FunctionTests.cs`: cobre a classe de entrada da Lambda e validações.

2. **Mocks com Moq**

   * Simula dependências como `IVideoStorage`, `IFrameExtractor`, `IJobStateRepository` etc.

3. **Testes de Falha**

   * Testes que simulam exceções (ex.: falha no download ou extração) para verificar tratamento correto de estado.

> Em caso de erros de validação (`IEnvironmentValidator`), use mocks ou configure as variáveis de ambiente localmente.

---

## Observabilidade

* **CloudWatch Logs** – Monitore logs detalhados na aba de Monitoramento da Lambda.
* **Fila de Progresso (SQS)** – Acompanhe o andamento e finalizações por mensagens.
* **Dead Letter Queue (DLQ)** – Falhas recorrentes são encaminhadas para análise posterior.
* **MongoDB** – Verifique documentos da coleção `jobs` para saber o estado atual de execução.
* **Sentry** – Integração para relatório de exceções.

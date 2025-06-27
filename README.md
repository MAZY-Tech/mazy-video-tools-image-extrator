# AWS Lambda de Extração e Compactação de Frames de Vídeo

Este projeto AWS Lambda é uma função robusta e tolerante a falhas, projetada para processar mensagens SQS contendo informações de vídeos, extrair frames em blocos, compactá-los em um arquivo ZIP e gerenciar o progresso e o estado do trabalho em um banco de dados MongoDB.

## Visão Geral do Projeto

A função `ImageExtractor` é acionada por mensagens em uma fila SQS. Para cada mensagem, ela executa as seguintes etapas:

1.  **Validação e Inicialização**: Carrega as configurações do ambiente e valida a mensagem SQS recebida.
    
2.  **Download do Vídeo**: Baixa o arquivo de vídeo original de um bucket S3.
    
3.  **Análise do Vídeo**: Utiliza o `ffprobe` para determinar a duração total e o número de frames do vídeo.
    
4.  **Extração de Frames em Blocos**:
    
    -   Divide o vídeo em blocos menores (ex: 10% da duração total, mínimo de 30 segundos).
        
    -   Para cada bloco, utiliza o `ffmpeg` para extrair frames individualmente.
        
    -   Faz o upload desses frames para um bucket S3 de destino, organizando-os por ID de vídeo e número do bloco.
        
    -   Atualiza o progresso do job no MongoDB e envia mensagens de progresso para uma fila SQS dedicada.
        
    -   Possui lógica de retomada (`LastProcessedSecond`, `CurrentBlock`) para continuar o processamento de onde parou em caso de interrupção ou falha.
        
5.  **Criação e Upload do ZIP**: Após todos os frames serem extraídos e enviados, os frames são recuperados do S3 e compactados em um arquivo ZIP. O ZIP final é então enviado para um bucket S3 de destino.
    
6.  **Conclusão**: Marca o job como `COMPLETED` no MongoDB e envia uma mensagem de conclusão para a fila de progresso SQS.
    

A função é projetada para ser idempotente, garantindo que o reprocessamento de mensagens SQS não duplique o trabalho já concluído e que falhas sejam gerenciadas via Dead Letter Queue (DLQ) do SQS para retentativas.

## Tecnologias Chave

-   **AWS Lambda**: Plataforma de computação serverless.
    
-   **Amazon SQS**: Serviço de fila de mensagens para acionamento e comunicação de progresso.
    
-   **Amazon S3**: Armazenamento de objetos para vídeos de entrada, frames extraídos e arquivos ZIP finais.
    
-   **MongoDB (ou compatível como Amazon DocumentDB)**: Banco de dados NoSQL para persistir o estado e o progresso de cada job.
    
-   **FFmpeg/FFprobe**: Ferramentas de linha de comando para processamento de áudio/vídeo, empacotadas com a Lambda.
    
-   **.NET 8 (ou superior)**: Framework para o desenvolvimento da função.
    

## Pré-requisitos

Para construir e implantar este projeto, você precisará:

-   [**.NET SDK 8.0 (ou superior)**](https://dotnet.microsoft.com/download "null")
    
-   **AWS CLI**
    
-   **Amazon.Lambda.Tools Global Tool**
    
-   **Binários FFmpeg e FFprobe** compilados para Linux.
    
-   **Uma instância MongoDB/DocumentDB**.
    
-   **Buckets S3** (entrada, frames, zips).
    
-   **Filas SQS** (principal e de progresso).
    


## Configuração (Variáveis de Ambiente)

A função Lambda utiliza as seguintes variáveis de ambiente. Elas devem ser configuradas na sua função Lambda na AWS:

| Variável | Descrição | Exemplo de Valor |
| :--- | :--- | :--- |
| `FRAMES_BUCKET_NAME` | Nome do bucket S3 onde os frames serão temporariamente armazenados. | `my-video-frames-bucket` |
| `ZIP_BUCKET_NAME` | Nome do bucket S3 onde os arquivos ZIP finais serão armazenados. | `my-video-zips-bucket` |
| `PROGRESS_QUEUE_URL` | URL da fila SQS para onde as mensagens de progresso e conclusão do job serão enviadas. | `https://sqs.us-east-1.amazonaws.com/123456789012/MyProgressQueue` |
| `MONGO_DB_HOST` | Hostname ou endereço IP do seu cluster MongoDB/DocumentDB. | `docdb-xxxx.cluster-abcdefghij.us-east-1.docdb.amazonaws.com` |
| `MONGO_DB_PORT` | Porta do seu cluster MongoDB/DocumentDB. | `27017` |
| `MONGO_DB_USER` | Usuário para autenticação no MongoDB/DocumentDB. | `lambdauser` |
| `MONGO_DB_PASSWORD` | Senha para autenticação no MongoDB/DocumentDB. | `mySecurePassword123` |
| `DATABASE_NAME` | Nome do banco de dados no MongoDB/DocumentDB para persistência do job. | `video_processing` |
| `COLLECTION_NAME` | Nome da coleção no MongoDB/DocumentDB para os documentos de job. | `jobs` |
  
## Uso e Estruturas de Dados

Para iniciar um processo, publique uma mensagem na fila SQS principal. A função irá então interagir com a fila de progresso e o MongoDB usando as seguintes estruturas.

### Fila SQS de Entrada (Ex: `incoming-queue`)

Esta é a mensagem que dispara a execução da Lambda. Ela corresponde à classe `MensagemSQS` no código.

```
{
  "video_id": "a4708f6c-b38a-41c2-a799-24f0f1e5f8f7",
  "video_hash": "sha1:4f9c1a38b8ed3f57a89c3cb674c64f2d9f23ec93",
  "cognito_user_id": "a4681438-70b1-70cb-308f-cc40ea50066a",
  "bucket": "mazy-video-tools-d32e914",
  "key": "a4681438-70b1-70cb-308f-cc40ea50066a/1750362343187_O DESAFIO MAIS IMPOSSÍVEL DO MUNDO! #shorts.mp4",
  "timestamp": "2025-06-19T19:45:50+00:00"
}

```

### Fila SQS de Progresso (Ex: `progress-queue`)

A função envia atualizações para esta fila durante e após o processamento.

**Em Progresso** (enviada após cada bloco):

```
{
  "video_id": "a4708f6c-b38a-41c2-a799-24f0f1e5f8f7",
  "status": "RUNNING",
  "progress": 20,
  "current_block": 2,
  "total_blocks": 10,
  "timestamp": "2025-06-19T19:46:37+00:00"
}

```

**Concluído** (enviada no final):

```
{
  "video_id": "a4708f6c-b38a-41c2-a799-24f0f1e5f8f7",
  "status": "COMPLETED",
  "progress": 100,
  "timestamp": "2025-06-19T19:48:05+00:00",
  "zip": {
    "bucket": "mazy-video-tools-video-zip-4d5e917",
    "key": "a4681438-70b1-70cb-308f-cc40ea50066a/a4708f6c-b38a-41c2-a799-24f0f1e5f8f7.zip"
  }
}

```

### Documento no MongoDB (Coleção `jobs`)

Este documento representa o estado completo de um job e é atualizado ao longo do processo.

```
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
  "current_step": "ExtraindoFrames",
  "started_at": "2025-06-19T19:40:00.000Z",
  "completed_at": null
}

```

_Status pode ser: `Pendente`, `Executando`, `Completo`, `Falhou`, `Interrompido`_.

## Monitoramento e Solução de Problemas

-   **Logs do CloudWatch**: Acesse a aba **Monitor** da sua função Lambda para visualizar os logs detalhados.
    
-   **Fila SQS de Progresso**: Monitore as mensagens para acompanhar o status em tempo real.
    
-   **SQS** Dead Letter Queue **(DLQ)**: Verifique a DLQ da fila principal para mensagens que falharam repetidamente.
    
-   **MongoDB**: Verifique a coleção `jobs` para inspecionar o estado detalhado de cada job.
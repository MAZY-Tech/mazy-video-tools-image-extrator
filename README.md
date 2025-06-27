# AWS Lambda de Extra��o e Compacta��o de Frames de V�deo

Este projeto AWS Lambda � uma fun��o robusta e tolerante a falhas, projetada para processar mensagens SQS contendo informa��es de v�deos, extrair frames em blocos, compact�-los em um arquivo ZIP e gerenciar o progresso e o estado do trabalho em um banco de dados MongoDB.

## Vis�o Geral do Projeto

A fun��o `ImageExtractor` � acionada por mensagens em uma fila SQS. Para cada mensagem, ela executa as seguintes etapas:

1.  **Valida��o e Inicializa��o**: Carrega as configura��es do ambiente e valida a mensagem SQS recebida.
    
2.  **Download do V�deo**: Baixa o arquivo de v�deo original de um bucket S3.
    
3.  **An�lise do V�deo**: Utiliza o `ffprobe` para determinar a dura��o total e o n�mero de frames do v�deo.
    
4.  **Extra��o de Frames em Blocos**:
    
    -   Divide o v�deo em blocos menores (ex: 10% da dura��o total, m�nimo de 30 segundos).
        
    -   Para cada bloco, utiliza o `ffmpeg` para extrair frames individualmente.
        
    -   Faz o upload desses frames para um bucket S3 de destino, organizando-os por ID de v�deo e n�mero do bloco.
        
    -   Atualiza o progresso do job no MongoDB e envia mensagens de progresso para uma fila SQS dedicada.
        
    -   Possui l�gica de retomada (`LastProcessedSecond`, `CurrentBlock`) para continuar o processamento de onde parou em caso de interrup��o ou falha.
        
5.  **Cria��o e Upload do ZIP**: Ap�s todos os frames serem extra�dos e enviados, os frames s�o recuperados do S3 e compactados em um arquivo ZIP. O ZIP final � ent�o enviado para um bucket S3 de destino.
    
6.  **Conclus�o**: Marca o job como `COMPLETED` no MongoDB e envia uma mensagem de conclus�o para a fila de progresso SQS.
    

A fun��o � projetada para ser idempotente, garantindo que o reprocessamento de mensagens SQS n�o duplique o trabalho j� conclu�do e que falhas sejam gerenciadas via Dead Letter Queue (DLQ) do SQS para retentativas.

## Tecnologias Chave

-   **AWS Lambda**: Plataforma de computa��o serverless.
    
-   **Amazon SQS**: Servi�o de fila de mensagens para acionamento e comunica��o de progresso.
    
-   **Amazon S3**: Armazenamento de objetos para v�deos de entrada, frames extra�dos e arquivos ZIP finais.
    
-   **MongoDB (ou compat�vel como Amazon DocumentDB)**: Banco de dados NoSQL para persistir o estado e o progresso de cada job.
    
-   **FFmpeg/FFprobe**: Ferramentas de linha de comando para processamento de �udio/v�deo, empacotadas com a Lambda.
    
-   **.NET 8 (ou superior)**: Framework para o desenvolvimento da fun��o.
    

## Pr�-requisitos

Para construir e implantar este projeto, voc� precisar�:

-   [**.NET SDK 8.0 (ou superior)**](https://dotnet.microsoft.com/download "null")
    
-   **AWS CLI**
    
-   **Amazon.Lambda.Tools Global Tool**
    
-   **Bin�rios FFmpeg e FFprobe** compilados para Linux.
    
-   **Uma inst�ncia MongoDB/DocumentDB**.
    
-   **Buckets S3** (entrada, frames, zips).
    
-   **Filas SQS** (principal e de progresso).
    


## Configura��o (Vari�veis de Ambiente)

A fun��o Lambda utiliza as seguintes vari�veis de ambiente. Elas devem ser configuradas na sua fun��o Lambda na AWS:

| Vari�vel | Descri��o | Exemplo de Valor |
| :--- | :--- | :--- |
| `FRAMES_BUCKET_NAME` | Nome do bucket S3 onde os frames ser�o temporariamente armazenados. | `my-video-frames-bucket` |
| `ZIP_BUCKET_NAME` | Nome do bucket S3 onde os arquivos ZIP finais ser�o armazenados. | `my-video-zips-bucket` |
| `PROGRESS_QUEUE_URL` | URL da fila SQS para onde as mensagens de progresso e conclus�o do job ser�o enviadas. | `https://sqs.us-east-1.amazonaws.com/123456789012/MyProgressQueue` |
| `MONGO_DB_HOST` | Hostname ou endere�o IP do seu cluster MongoDB/DocumentDB. | `docdb-xxxx.cluster-abcdefghij.us-east-1.docdb.amazonaws.com` |
| `MONGO_DB_PORT` | Porta do seu cluster MongoDB/DocumentDB. | `27017` |
| `MONGO_DB_USER` | Usu�rio para autentica��o no MongoDB/DocumentDB. | `lambdauser` |
| `MONGO_DB_PASSWORD` | Senha para autentica��o no MongoDB/DocumentDB. | `mySecurePassword123` |
| `DATABASE_NAME` | Nome do banco de dados no MongoDB/DocumentDB para persist�ncia do job. | `video_processing` |
| `COLLECTION_NAME` | Nome da cole��o no MongoDB/DocumentDB para os documentos de job. | `jobs` |
  
## Uso e Estruturas de Dados

Para iniciar um processo, publique uma mensagem na fila SQS principal. A fun��o ir� ent�o interagir com a fila de progresso e o MongoDB usando as seguintes estruturas.

### Fila SQS de Entrada (Ex: `incoming-queue`)

Esta � a mensagem que dispara a execu��o da Lambda. Ela corresponde � classe `MensagemSQS` no c�digo.

```
{
  "video_id": "a4708f6c-b38a-41c2-a799-24f0f1e5f8f7",
  "video_hash": "sha1:4f9c1a38b8ed3f57a89c3cb674c64f2d9f23ec93",
  "cognito_user_id": "a4681438-70b1-70cb-308f-cc40ea50066a",
  "bucket": "mazy-video-tools-d32e914",
  "key": "a4681438-70b1-70cb-308f-cc40ea50066a/1750362343187_O DESAFIO MAIS IMPOSS�VEL DO MUNDO! #shorts.mp4",
  "timestamp": "2025-06-19T19:45:50+00:00"
}

```

### Fila SQS de Progresso (Ex: `progress-queue`)

A fun��o envia atualiza��es para esta fila durante e ap�s o processamento.

**Em Progresso** (enviada ap�s cada bloco):

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

**Conclu�do** (enviada no final):

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

### Documento no MongoDB (Cole��o `jobs`)

Este documento representa o estado completo de um job e � atualizado ao longo do processo.

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

## Monitoramento e Solu��o de Problemas

-   **Logs do CloudWatch**: Acesse a aba **Monitor** da sua fun��o Lambda para visualizar os logs detalhados.
    
-   **Fila SQS de Progresso**: Monitore as mensagens para acompanhar o status em tempo real.
    
-   **SQS** Dead Letter Queue **(DLQ)**: Verifique a DLQ da fila principal para mensagens que falharam repetidamente.
    
-   **MongoDB**: Verifique a cole��o `jobs` para inspecionar o estado detalhado de cada job.
# aspnetcore10-otel-grafanacloud-tempo-loki-postgres-mysql-testcontainers_contagemacessos
Exemplo de uso de OpenTelemetry + Grafana Cloud + Tempo (trace) + Loki (logs) em uma API REST de contagem de acessos baseada em .NET 10 + ASP.NET Core e que utiliza bases de dados PostgreSQL + MySql + Testcontainers.

## Testes

Para integrar esta aplicação com o **Grafana Cloud** deve-se acessar a **opção OpenTelemetry**:

![OpenTelemetry no Grafana Cloud](img/grafana-cloud-01.png)

Acessar em **Password / API Token** a opção **Generate now**:

![Gerando novo token](img/grafana-cloud-02.png)

Criando assim um novo token (inserido no appsettings.json para este exemplo):

![Novo token gerado](img/grafana-cloud-03.png)

Logs gerados e exportados para o Grafana Loki:

![Logs no Grafana Loki](img/loki-01.png)

Trace do Grafana Tempo acessando uma base PostgreSQL:

![Trace acessando Postgres](img/trace-01.png)

Trace do Grafana Tempo acessando uma base MySQL:

![Trace acessando MySQL](img/trace-02.png)
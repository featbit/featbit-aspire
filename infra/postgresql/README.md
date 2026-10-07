# Local PostgreSQL initialization

`6.0.0/docker-entrypoint-initdb.d` contains the unmodified SQL scripts from
[FeatBit 6.0.0](https://github.com/featbit/featbit/tree/6.0.0/infra/postgresql/docker-entrypoint-initdb.d).
Their Git blob checksums were verified against the release tag.

During local runs, Aspire copies this version's scripts into the PostgreSQL
container. The official entrypoint runs them in filename order only when its data
volume is empty. They create the `featbit` database, schema, and sample data. Local
runs use PostgreSQL `15.10`, matching the release's Docker Compose configuration.

Existing volumes retain their data and are not automatically migrated. When
changing the FeatBit version, add that release's scripts under the matching version
directory and handle any existing local database migration explicitly. External
databases used by publish/deploy or by an opted-in local run are never initialized
by this AppHost.

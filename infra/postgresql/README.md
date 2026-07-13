# FeatBit PostgreSQL initialization

The SQL files under `docker-entrypoint-initdb.d` are copied unchanged from the
[FeatBit 5.4.4 release](https://github.com/featbit/featbit/tree/5.4.4/infra/postgresql/docker-entrypoint-initdb.d).
They are mounted only into the local PostgreSQL container and initialize the `featbit`
database through schema version 5.4.1.

Source copyright: FeatBit contributors. Licensed under the MIT License; see the
repository root `LICENSE` file.

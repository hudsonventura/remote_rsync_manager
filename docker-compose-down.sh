#!/bin/bash

COMPOSE_FILES=(-f docker-compose.yml)

if [[ -f docker-compose.override.yml ]]; then
    COMPOSE_FILES+=(-f docker-compose.override.yml)
elif [[ -f docker-compose-override.yml ]]; then
    COMPOSE_FILES+=(-f docker-compose-override.yml)
fi

docker compose "${COMPOSE_FILES[@]}" down


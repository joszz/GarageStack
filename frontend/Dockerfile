FROM node:24-alpine AS build
WORKDIR /app
ARG VITE_DEMO_MODE=false
ENV VITE_DEMO_MODE=$VITE_DEMO_MODE
RUN apk upgrade --no-cache && corepack enable && corepack prepare pnpm@11.1.3 --activate
COPY package.json pnpm-lock.yaml ./
RUN pnpm config set minimum-release-age 0 && pnpm install --frozen-lockfile --ignore-scripts
COPY . .
RUN pnpm run build

FROM nginx:alpine AS runtime
RUN apk upgrade --no-cache
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80

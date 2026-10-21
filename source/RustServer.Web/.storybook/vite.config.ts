import { defineConfig } from 'vite'

/**
 * Storybook has no backend requests or development-server proxy. Keep this
 * separate from the application Vite config, which validates live topology.
 */
export default defineConfig({})

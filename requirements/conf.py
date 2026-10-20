project = 'Toolkit requirement traceability'
extensions = ['sphinx_needs']
needs_from_toml = 'ubproject.toml'
needs_build_json = True
root_doc = 'index'
language = 'zh_CN'
exclude_patterns = ['.venv/**', '.ub_cache/**']

def configure_sources(app, config):
    config.needs_import_keys['toolkit_sources'] = config.toolkit_sources_file

def setup(app):
    app.add_config_value('toolkit_sources_file', '../_build/requirements/sources.json', 'env', types=[str])
    app.connect('config-inited', configure_sources, priority=20)

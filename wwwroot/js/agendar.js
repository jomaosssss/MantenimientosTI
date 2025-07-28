function enviarDatos() {
    const input = document.getElementById("ArchivoCsv");
    const resultadoDetallado = document.getElementById("resultadoDetallado");

    // Ocultar resultados previos
    resultadoDetallado.classList.add('d-none');

    // Validar que se haya seleccionado un archivo
    if (!input.files || input.files.length === 0) {
        Swal.fire({
            icon: 'error',
            title: 'Archivo requerido',
            text: 'Por favor seleccione un archivo CSV',
            confirmButtonColor: '#3085d6'
        });
        return;
    }

    const formData = new FormData();
    formData.append("ArchivoCsv", input.files[0]);

    // Mostrar loading en el botón
    const btn = document.querySelector(".btn-success");
    const originalBtnContent = btn.innerHTML;
    btn.innerHTML = '<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span> Procesando...';
    btn.disabled = true;

    fetch("/Agenda/EnviarDatosCsv", {
        method: "POST",
        body: formData
    })
        .then(response => {
            if (!response.ok) {
                throw new Error('Error en la respuesta del servidor');
            }
            return response.json();
        })
        .then(data => {
            if (data.success) {
                // Construir el mensaje de resumen con todos los posibles casos
                let resumenParts = [];
                resumenParts.push(`${data.resumen.exitosos} registros exitosos`);

                if (data.resumen.errores > 0) resumenParts.push(`${data.resumen.errores} errores`);
                if (data.resumen.duplicados > 0) resumenParts.push(`${data.resumen.duplicados} duplicados`);
                if (data.resumen.activosNoExistentes > 0) resumenParts.push(`${data.resumen.activosNoExistentes} equipos no existentes`);
                if (data.resumen.activosOtraZona > 0) resumenParts.push(`${data.resumen.activosOtraZona} equipos de otra zona`);
                if (data.resumen.fechasPasadas > 0) resumenParts.push(`${data.resumen.fechasPasadas} fechas vencidas`);

                Swal.fire({
                    icon: 'success',
                    title: '¡Éxito!',
                    html: `<p>${data.message}</p>
                      <p class="small text-muted mt-2">
                        <strong>Resumen:</strong> ${resumenParts.join(', ')}
                      </p>`,
                    confirmButtonColor: '#3085d6',
                    showCancelButton: false,
                    allowOutsideClick: false
                });

                mostrarResultadoDetallado(data);
            } else {
                // Mostrar SweetAlert con errores
                let mensajeError = data.message;

                if (data.detallesErrores && data.detallesErrores.length > 0) {
                    mensajeError += `<br><br><strong>Errores encontrados:</strong><ul class="text-start small">`;
                    data.detallesErrores.slice(0, 5).forEach(error => {
                        mensajeError += `<li>${error}</li>`;
                    });
                    if (data.totalErrores > 5) {
                        mensajeError += `<li>... y ${data.totalErrores - 5} errores más</li>`;
                    }
                    mensajeError += `</ul>`;
                }

                Swal.fire({
                    icon: 'error',
                    title: 'Error',
                    html: mensajeError,
                    confirmButtonColor: '#3085d6'
                });

                // Mostrar resultados detallados
                if (data.lineasProcesadasDetalle) {
                    mostrarResultadoDetallado(data);
                }
            }
        })
        .catch(error => {
            Swal.fire({
                icon: 'error',
                title: 'Error de conexión',
                text: 'Error al conectar con el servidor: ' + error.message,
                confirmButtonColor: '#3085d6'
            });
        })
        .finally(() => {
            // Restaurar el botón
            btn.innerHTML = originalBtnContent;
            btn.disabled = false;
        });
}

function mostrarResultadoDetallado(data) {
    const resultadoDetallado = document.getElementById("resultadoDetallado");
    const detalleLineas = document.getElementById("detalleLineas");

    // Mostrar el resumen SIEMPRE, independientemente de si hay éxito o no
    if (data.resumen) {
        document.getElementById("totalLineas").textContent = data.resumen.totalLineas;
        document.getElementById("exitosos").textContent = data.resumen.exitosos;
        document.getElementById("errores").textContent = data.resumen.errores;
        document.getElementById("duplicados").textContent = data.resumen.duplicados;
        document.getElementById("duplicadosBD").textContent = data.resumen.duplicadosBD;
        document.getElementById("duplicadosArchivo").textContent = data.resumen.duplicadosArchivo;
        document.getElementById("activosNoExistentes").textContent = data.resumen.activosNoExistentes;
        document.getElementById("fechasPasadas").textContent = data.resumen.fechasPasadas;
        document.getElementById("activosOtraZona").textContent = data.resumen.activosOtraZona;
    }

    // Mostrar detalle de todas las líneas procesadas
    if (data.lineasProcesadasDetalle && data.lineasProcesadasDetalle.length > 0) {
        detalleLineas.innerHTML = '';

        const table = document.createElement('table');
        table.className = 'table table-sm table-bordered small';

        // Crear encabezado
        const thead = document.createElement('thead');
        thead.innerHTML = `
            <tr>
                <th>#</th>
                <th>Contenido</th>
                <th>Estado</th>
                <th>Mensaje</th>
            </tr>
        `;
        table.appendChild(thead);

        // Crear cuerpo
        const tbody = document.createElement('tbody');

        data.lineasProcesadasDetalle.forEach(linea => {
            const row = document.createElement('tr');

            // Determinar clase CSS según estado
            let rowClass = '';
            if (linea.estado === 'exitoso') rowClass = 'table-success';
            if (linea.estado === 'error') rowClass = 'table-danger';
            if (linea.estado === 'ignorado') rowClass = 'table-secondary';

            row.className = rowClass;
            row.innerHTML = `
                <td>${linea.numero}</td>
                <td>${linea.contenido}</td>
                <td>${linea.estado.toUpperCase()}</td>
                <td>${linea.mensaje}</td>
            `;
            tbody.appendChild(row);
        });

        table.appendChild(tbody);
        detalleLineas.appendChild(table);
    } else {
        detalleLineas.innerHTML = '<p class="text-muted mb-0">No hay información de líneas que mostrar</p>';
    }

    // Mostrar el contenedor
    resultadoDetallado.classList.remove('d-none');
}
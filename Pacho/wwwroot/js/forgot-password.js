import emailjs from 'https://cdn.emailjs.com/dist/email.min.mjs';

document.addEventListener('DOMContentLoaded', () => {
    const form = document.getElementById('forgotPasswordForm');
    const emailInput = document.getElementById('email');
    const messageBox = document.getElementById('messageBox');

    form.addEventListener('submit', async (e) => {
        e.preventDefault();

        const email = emailInput.value;

        try {
            const response = await fetch('/Account/ForgotPasswordToken', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]').value
                },
                body: JSON.stringify({ email })
            });

            const result = await response.json();

            if (result.success) {
                await emailjs.send("service_o81ify9", "template_wz3s7z", {
                    user_email: email,
                    user_name: result.name,
                    reset_link: result.resetLink
                }, "ubdwGglPssCLbO0Ez");

                messageBox.innerHTML = `<span style="color:green">Correo enviado correctamente. Revisa tu bandeja de entrada.</span>`;
            } else {
                messageBox.innerHTML = `<span style="color:red">${result.message}</span>`;
            }

        } catch (error) {
            console.error('Error enviando email:', error);
            messageBox.innerHTML = `<span style="color:red">Hubo un error al enviar el correo. Intenta nuevamente.</span>`;
        }
    });
});
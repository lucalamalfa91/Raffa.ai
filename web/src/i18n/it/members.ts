/**
 * Italian for Workspace & members (route `/workspace/members`): the page, the roster table, the
 * invite pane and the request-access pane. Keys are the English text the screen renders.
 */

export const IT_MEMBERS: Readonly<Record<string, string>> = {
  // Page and guide
  "Setup": "Configurazione",
  "Invite a colleague, or change who can do what.": "Invita un collega o cambia chi può fare cosa.",
  "Ask a Workspace Admin to invite your colleagues.": "Chiedi a un Workspace Admin di invitare i tuoi colleghi.",
  "Procurement members ask, upload and work the renewals; a Workspace Admin also deletes documents and manages members.":
    "I membri Procurement fanno domande, caricano contratti e lavorano i rinnovi; un Workspace Admin elimina anche documenti e gestisce i membri.",
  "The invited colleague signs in with the link and lands in this workspace.": "Il collega invitato accede con il link ed entra in questo workspace.",
  "{name} · tenant {id}": "{name} · tenant {id}",
  "tenant {id}": "tenant {id}",
  "No workspace selected": "Nessun workspace selezionato",
  "Choose a workspace before managing members.": "Scegli un workspace prima di gestire i membri.",
  "Tip: invite the team once the first contract is validated — there is nothing for them to ask before that.":
    "Suggerimento: invita il team dopo aver validato il primo contratto — prima non c’è nulla da chiedere.",
  "Loading members…": "Carico i membri…",
  "Members unavailable": "Membri non disponibili",
  "The setting could not be saved.": "Non sono riuscito a salvare l’impostazione.",
  "The roster could not be loaded.": "Non riesco a caricare l’elenco dei membri.",
  "The invitation could not be created.": "Non sono riuscito a creare l’invito.",
  "The invitation could not be revoked.": "Non sono riuscito a revocare l’invito.",
  "The member could not be removed.": "Non sono riuscito a rimuovere il membro.",

  // Ask Raffa setting
  "Allow Ask Raffa to search the public web": "Consenti a Chiedi a Raffa di cercare sul web pubblico",
  "Off by default. When on, everyone here can switch on Web search in Ask Raffa’s composer: questions then go to the public web and to your contracts together, and a personal question is pointed at Google or an AI search tool. With the toggle off, Raffa may still offer a web search for a procurement question and asks permission first. Only the question’s words are searched — nothing from your contracts leaves Raffa; web results are labelled unverified.":
    "Disattivata di default. Se attiva, chiunque qui può accendere la ricerca web nel campo di Chiedi a Raffa: le domande vanno allora al web pubblico e ai tuoi contratti insieme, e una domanda personale viene indirizzata a Google o a uno strumento di ricerca AI. Con l’interruttore spento, Raffa può comunque proporre una ricerca web per una domanda di procurement e chiede prima il permesso. Si cercano solo le parole della domanda — nulla dei tuoi contratti esce da Raffa; i risultati web sono segnati come non verificati.",
  "Only a Workspace Admin can change this.": "Solo un Workspace Admin può modificarla.",

  // Roster table
  "Member": "Membro",
  "Role": "Ruolo",
  "Actions": "Azioni",
  "Workspace Admin": "Workspace Admin",
  "Procurement": "Procurement",
  "Invited⟦member⟧": "Invitato",
  "Expired⟦member⟧": "Scaduto",
  "Active⟦member⟧": "Attivo",
  "Send a new invitation": "Invia un nuovo invito",
  "Revoke": "Revoca",
  "Remove": "Rimuovi",
  "Yes, revoke": "Sì, revoca",
  "Revoking…": "Revoca in corso…",
  "Yes, send a new invitation": "Sì, invia un nuovo invito",
  "Sending…": "Invio in corso…",
  "Yes, remove": "Sì, rimuovi",
  "Removing…": "Rimozione in corso…",
  "This is the last Workspace Admin. Invite another Workspace Admin first.": "È l’ultimo Workspace Admin. Invita prima un altro Workspace Admin.",
  "Revoke the invitation for {email}?": "Revocare l’invito per {email}?",
  "Their link stops working. They never had access to this workspace.": "Il suo link smette di funzionare. Non ha mai avuto accesso a questo workspace.",
  "Send a new invitation to {email}?": "Inviare un nuovo invito a {email}?",
  "The link you already shared stops working.": "Il link che hai già condiviso smette di funzionare.",
  "Remove yourself from this workspace?": "Rimuoverti da questo workspace?",
  "You will lose access immediately and will need a new invitation to return.": "Perderai subito l’accesso e servirà un nuovo invito per rientrare.",
  "Remove {email}?": "Rimuovere {email}?",
  "They lose access immediately. To bring them back you will need to send a new invitation.":
    "Perde subito l’accesso. Per riaverlo dovrai inviare un nuovo invito.",

  // Invite pane
  "Invite a colleague": "Invita un collega",
  "Work email": "Email di lavoro",
  "Asks, uploads, reviews, triages renewals": "Fa domande, carica, verifica, smista i rinnovi",
  "Also deletes documents and manages members": "Elimina anche documenti e gestisce i membri",
  "Send invitation": "Invia l’invito",
  "An email is required.": "Serve un indirizzo email.",
  "Enter a valid email address.": "Inserisci un indirizzo email valido.",
  "{email} is outside {domain}. They will get full {role} access to this workspace.":
    "{email} è fuori da {domain}. Avrà pieno accesso {role} a questo workspace.",
  "Invitation sent to {email}.": "Invito inviato a {email}.",
  "Invitation created, but the email could not be sent.": "Invito creato, ma non sono riuscito a inviare l’email.",
  "Invitation ready for {email}.": "Invito pronto per {email}.",
  "Invitation link": "Link d’invito",
  "Copied⟦link⟧": "Copiato",
  "Copy link": "Copia il link",
  "It expires {date}, can be used once, and is not shown again.": "Scade il {date}, si può usare una sola volta e non viene mostrato di nuovo.",
  "They will get a one-time code from Microsoft the first time they sign in.": "Al primo accesso riceverà un codice monouso da Microsoft.",
  "No invitation was created.": "Nessun invito è stato creato.",
  "Raffa.ai is not allowed to add guests to your company directory yet. A tenant administrator has to approve that permission.":
    "Raffa.ai non è ancora autorizzato ad aggiungere ospiti alla directory aziendale. Un amministratore del tenant deve approvare questo permesso.",
  "Your company directory would not add {email}. Check the address, or ask a tenant administrator.":
    "La directory aziendale non ha aggiunto {email}. Controlla l’indirizzo o chiedi a un amministratore del tenant.",
  "Your company directory could not be reached. Try again in a few minutes.": "Non riesco a raggiungere la directory aziendale. Riprova tra qualche minuto.",
  "Raffa.ai could not create this invitation.": "Raffa.ai non è riuscito a creare questo invito.",

  // Request access (Procurement)
  "Request access": "Richiedi l’accesso",
  "You don’t manage this workspace": "Non gestisci questo workspace",
  "Only a Workspace Admin can invite, revoke or remove members. Ask one of your workspace’s Admins for access.":
    "Solo un Workspace Admin può invitare, revocare o rimuovere membri. Chiedi l’accesso a uno degli Admin del tuo workspace.",
  "Workspace Admin access — {name}": "Accesso Workspace Admin — {name}",
  "Workspace Admin access": "Accesso Workspace Admin",

  // Info tips ("i") on the page (components/infoTipCopy.ts)
  "How members work": "Come funzionano i membri",
  "Invite your colleagues with their work email. Everyone in the workspace works on the same documents and contracts; chats stay personal.":
    "Invita i colleghi con la loro email di lavoro. Tutti nel workspace lavorano sugli stessi documenti e contratti; le chat restano personali.",
  "The table shows who has joined and who is still invited. From it you can revoke an invitation, issue a new one, or remove someone.":
    "La tabella mostra chi è entrato e chi è ancora invitato. Da qui puoi revocare un invito, inviarne uno nuovo o rimuovere qualcuno.",
  "What the roles can do": "Cosa può fare ogni ruolo",
  "Procurement: asks Raffa, uploads and reviews documents, handles renewals.": "Procurement: fa domande a Raffa, carica e verifica documenti, gestisce i rinnovi.",
  "Workspace Admin: all of that, plus deleting documents and managing members.": "Workspace Admin: tutto questo, in più elimina documenti e gestisce i membri.",
  "What the statuses mean": "Cosa significano gli stati",
  "Invited: the invitation is out, not accepted yet. Active: the person has joined. Expired: the invitation ran out before it was used.":
    "Invitato: l’invito è partito ma non è ancora accettato. Attivo: la persona è entrata. Scaduto: l’invito è scaduto prima di essere usato.",
  "How invitations work": "Come funzionano gli inviti",
  "The person gets a link by email to join this workspace. If the email cannot go out, copy the link and share it yourself.":
    "La persona riceve via email un link per entrare in questo workspace. Se l’email non parte, copia il link e condividilo tu.",
  "The link works once and expires. Until it is used, the person shows as Invited, and you can issue a new invitation or revoke it.":
    "Il link funziona una volta sola e scade. Finché non viene usato, la persona risulta Invitata e puoi inviare un nuovo invito o revocarlo.",
};

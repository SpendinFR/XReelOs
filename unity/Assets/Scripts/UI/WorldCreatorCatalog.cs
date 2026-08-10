using System;
using System.Collections.Generic;
using UnityEngine;

namespace MLOmega.XR.UI
{
    /// <summary>
    /// Procedural catalogue: a compact set of audited render grammars expands to
    /// hundreds of visual presets without shipping hundreds of heavy meshes.
    /// </summary>
    public static class WorldCreatorCatalog
    {
        [Serializable]
        public sealed class Entry
        {
            public string presetId;
            public string categoryId;
            public string archetypeId;
            public string templateId;
            public string styleId;
            public string animationId;
            public string accentHex;
            public string secondaryHex;
            public string label;
            public string subtitle;
            public Vector3 defaultScale;
        }

        private sealed class Grammar
        {
            public string Key;
            public string Category;
            public string Template;
            public string Label;
            public string Subtitle;
            public Vector3 Scale;
        }

        private static readonly string[][] Palettes =
        {
            new[] { "18E8FF", "7B3CFF", "cyan-violet" },
            new[] { "FF3EC8", "42F5E9", "neon-pop" },
            new[] { "FF6A24", "FFD447", "amber-fire" },
            new[] { "45FF8A", "16A6FF", "mint-data" },
            new[] { "B56CFF", "FF4D8D", "dreamwave" },
            new[] { "F4F8FF", "26C6FF", "ice-white" },
            new[] { "FF334F", "FFB020", "alert-red" },
            new[] { "84FFEA", "153BFF", "deep-cyan" },
        };

        private static readonly string[] Animations =
        {
            "soft_pulse", "scan", "orbit", "data_rain"
        };

        private static readonly Grammar[] Grammars =
        {
            // Ville et architecture.
            G("shop-neon", "urban", "neon_sign", "ENSEIGNE", "NÉON URBAIN", 1.1f, 1f, 1f),
            G("street-name", "urban", "neon_sign", "RUE", "SIGNALÉTIQUE SUSPENDUE", 1.35f, .8f, 1f),
            G("facade-ad", "urban", "holo_billboard", "PUBLICITÉ", "ÉCRAN TRANSPARENT", 1.5f, 1.2f, 1f),
            G("building-crown", "urban", "building_crown", "BÂTIMENT", "COURONNE DE LUMIÈRE", 2f, 1.4f, 2f),
            G("district-totem", "urban", "street_totem", "QUARTIER", "TOTEM D'INFORMATION", 1f, 1.8f, 1f),
            G("brand-orbit", "urban", "logo_orbit", "ENSEIGNE", "LOGO ORBITAL", 1.2f, 1.2f, 1.2f),
            G("alley-gate", "urban", "portal_arch", "PASSAGE", "PORTAIL DE RUELLE", 1.8f, 2f, 1.2f),
            G("rooftop-beacon", "urban", "poi_beacon", "TOIT", "BALISE VERTICALE", 1.4f, 2f, 1.4f),
            G("crosswalk-pulse", "urban", "direction_arrow", "TRAVERSÉE", "FLUX PIÉTON", 1.5f, .65f, 2f),
            G("construction-zone", "urban", "warning_barrier", "CHANTIER", "PÉRIMÈTRE ACTIF", 2f, 1f, 1.2f),
            G("streetlight-halo", "urban", "logo_orbit", "LUMIÈRE", "HALO DE RUE", .9f, 1.5f, .9f),
            G("facade-outline", "urban", "room_boundary", "FAÇADE", "CONTOUR ARCHITECTURAL", 2.5f, 2f, 1.2f),
            G("city-data-ticker", "urban", "window_display", "VILLE", "FLUX D'INFORMATION", 1.8f, 1f, 1f),
            G("parking-marker", "urban", "poi_beacon", "PARKING", "PLACE MÉMORISÉE", 1.1f, 1.1f, 1.1f),

            // Maison et appartement.
            G("home-dashboard", "home", "home_widget", "MAISON", "PANNEAU AMBIANT", .85f, .85f, .85f),
            G("room-frame", "home", "room_boundary", "PIÈCE", "LIMITE HOLOGRAPHIQUE", 1.8f, 1f, 1.8f),
            G("spatial-note", "home", "annotation", "NOTE", "ANNOTATION SPATIALE", .8f, .8f, .8f),
            G("memory-ripple", "home", "memory_echo", "SOUVENIR", "ÉCHO VISUEL", 1f, 1f, 1f),
            G("door-portal", "home", "portal_arch", "PORTE", "SEUIL LUMINEUX", 1.2f, 1.8f, .8f),
            G("appliance-halo", "home", "logo_orbit", "APPAREIL", "COMMANDE CONNECTÉE", .65f, .65f, .65f),
            G("shelf-index", "home", "street_totem", "RANGEMENT", "INDEX DE CONTENU", .7f, 1.1f, .7f),
            G("ambient-clock", "home", "logo_orbit", "HEURE", "HORLOGE ORBITALE", .75f, .75f, .75f),
            G("music-equalizer", "home", "particle_column", "AUDIO", "ÉGALISEUR AMBIANT", 1.2f, 1.1f, .8f),
            G("window-vista", "home", "window_display", "FENÊTRE", "VUE AUGMENTÉE", 1.5f, 1.2f, 1f),
            G("task-board", "home", "holo_billboard", "TÂCHES", "TABLEAU DE MAISON", 1.1f, .9f, 1f),
            G("plant-aura", "home", "particle_column", "PLANTE", "AURA ORGANIQUE", .7f, 1.2f, .7f),
            G("mirror-frame", "home", "room_boundary", "MIROIR", "CADRE SYNTHÉTIQUE", 1f, 1.5f, .5f),
            G("floor-guidance", "home", "direction_arrow", "PARCOURS", "FIL D'ARIANE", 1f, .5f, 1.5f),

            // Navigation et mobilité.
            G("route-arrow", "navigation", "direction_arrow", "DIRECTION", "CHEMIN FLOTTANT", 1.2f, 1.2f, 1.2f),
            G("poi-destination", "navigation", "poi_beacon", "DESTINATION", "POINT D'INTÉRÊT", 1.2f, 1.2f, 1.2f),
            G("arrival-portal", "navigation", "portal_arch", "ARRIVÉE", "PORTAIL DE DESTINATION", 1.8f, 2.2f, 1.2f),
            G("breadcrumb-ring", "navigation", "logo_orbit", "ÉTAPE", "REPÈRE DE PARCOURS", .7f, .7f, .7f),
            G("turn-chevron", "navigation", "direction_arrow", "TOURNER", "VIRAGE IMMINENT", .9f, .9f, .9f),
            G("elevation-marker", "navigation", "street_totem", "NIVEAU", "CHANGEMENT D'ÉTAGE", .8f, 1.4f, .8f),
            G("exit-gate", "navigation", "portal_arch", "SORTIE", "ISSUE LA PLUS PROCHE", 1.5f, 2f, 1f),
            G("distance-ring", "navigation", "logo_orbit", "DISTANCE", "COMPTEUR SPATIAL", .8f, .8f, .8f),
            G("vehicle-trail", "mobility", "vehicle_fx", "MOBILITÉ", "TRAÎNÉE ÉNERGÉTIQUE", 1.4f, 1f, 1.8f),
            G("charging-aura", "mobility", "particle_column", "RECHARGE", "ÉNERGIE DISPONIBLE", .9f, 1.3f, .9f),
            G("bike-beacon", "mobility", "poi_beacon", "VÉLO", "MOBILITÉ DOUCE", .8f, 1f, .8f),
            G("transit-door", "mobility", "portal_arch", "TRANSPORT", "PORTE D'ACCÈS", 1.4f, 1.8f, 1f),
            G("speed-lane", "mobility", "direction_arrow", "FLUX", "COULOIR DE DÉPLACEMENT", 1.2f, .7f, 2f),

            // Commerces, informations et services.
            G("store-window", "commerce", "window_display", "VITRINE", "PRÉSENTATION HOLOGRAPHIQUE", 1.5f, 1.4f, 1f),
            G("product-card", "commerce", "holo_billboard", "OFFRE", "CARTE PRODUIT", 1.1f, .9f, 1f),
            G("restaurant-menu", "commerce", "holo_billboard", "MENU", "CARTE FLOTTANTE", 1.2f, 1.1f, 1f),
            G("price-ribbon", "commerce", "neon_sign", "PRIX", "RUBAN COMPARATIF", 1f, .7f, 1f),
            G("product-pedestal", "commerce", "poi_beacon", "PRODUIT", "SOCLE DE PRÉSENTATION", .9f, 1.1f, .9f),
            G("sale-badge", "commerce", "logo_orbit", "OFFRE", "BADGE PROMOTIONNEL", .65f, .65f, .65f),
            G("review-halo", "commerce", "logo_orbit", "AVIS", "RÉPUTATION PUBLIQUE", .8f, .8f, .8f),
            G("open-status", "commerce", "neon_sign", "OUVERT", "STATUT DU LIEU", .9f, .7f, 1f),
            G("queue-guide", "commerce", "direction_arrow", "FILE", "GUIDAGE D'ATTENTE", 1f, .6f, 1.4f),
            G("pickup-beacon", "commerce", "poi_beacon", "RETRAIT", "POINT DE COLLECTE", .9f, 1.3f, .9f),
            G("news-ticker", "information", "window_display", "ACTUALITÉ", "BANDEAU CONTEXTUEL", 1.5f, .8f, 1f),
            G("event-card", "information", "holo_billboard", "ÉVÉNEMENT", "RENDEZ-VOUS À PROXIMITÉ", 1.1f, .9f, 1f),
            G("context-hint", "information", "annotation", "INFO", "CONNAISSANCE CONTEXTUELLE", .85f, .85f, .85f),
            G("safety-alert", "information", "warning_barrier", "ATTENTION", "SIGNAL CONTEXTUEL", 1.3f, .9f, 1f),

            // Décor cinéma FreeGuy / Blade Runner.
            G("patrol-drone", "cinematic", "sky_drone", "DRONE", "PATROUILLE HOLOGRAPHIQUE", 1.3f, 1.3f, 1.3f),
            G("giant-ad", "cinematic", "giant_hologram", "HOLOGRAMME", "SILHOUETTE MONUMENTALE", 3f, 3.8f, 2f),
            G("energy-column", "cinematic", "particle_column", "ÉNERGIE", "COLONNE DE PARTICULES", 1.4f, 2.4f, 1.4f),
            G("security-barrier", "cinematic", "warning_barrier", "ZONE", "BARRIÈRE DYNAMIQUE", 1.8f, 1.2f, 1f),
            G("data-rain-wall", "cinematic", "window_display", "DATA", "PLUIE NUMÉRIQUE", 1.8f, 1.7f, 1f),
            G("skyline-ribbon", "cinematic", "neon_sign", "HORIZON", "RUBAN DE LUMIÈRE", 2.5f, 1f, 1f),
            G("vortex-gate", "cinematic", "portal_arch", "PASSAGE", "VORTEX SYNTHÉTIQUE", 1.8f, 2.3f, 1.5f),
            G("particle-swarm", "cinematic", "particle_column", "ESSAIM", "PARTICULES VOLUMÉTRIQUES", 1.8f, 1.8f, 1.8f),
            G("holo-tree", "cinematic", "giant_hologram", "JARDIN", "ARBRE DE LUMIÈRE", 1.8f, 2.6f, 1.8f),
            G("flying-billboard", "cinematic", "sky_drone", "PUB", "PANNEAU VOLANT", 1.7f, 1.2f, 1.2f),
            G("constellation-mark", "cinematic", "logo_orbit", "CIEL", "SIGNE CÉLESTE", 1.5f, 1.5f, 1.5f),
            G("holo-statue", "cinematic", "giant_hologram", "STATUE", "MONUMENT HOLOGRAPHIQUE", 2.2f, 3f, 2.2f),
            G("giant-figure", "cinematic", "giant_hologram", "FIGURE", "PRÉSENCE MONUMENTALE", 3f, 4f, 2f),
            G("holo-creature", "cinematic", "giant_hologram", "CRÉATURE", "APPARITION DE LUMIÈRE", 2.5f, 2.5f, 2.5f),
            G("sky-koi", "cinematic", "sky_drone", "KOÏ", "CRÉATURE AÉRIENNE", 2f, 1.2f, 2.8f),
            G("air-traffic-lane", "cinematic", "vehicle_fx", "TRAFIC", "RAIL AÉRIEN", 2f, 1f, 3f),
            G("police-drone", "cinematic", "sky_drone", "SÉCURITÉ", "DRONE DE SURVEILLANCE", 1.2f, 1.2f, 1.2f),
            G("delivery-drone", "cinematic", "sky_drone", "LIVRAISON", "COULOIR LOGISTIQUE", 1f, 1f, 1f),
            G("floating-logo", "cinematic", "logo_orbit", "LOGO", "MARQUE HOLOGRAPHIQUE", 1.5f, 1.5f, 1.5f),
            G("mega-billboard", "cinematic", "holo_billboard", "MÉDIA", "ÉCRAN URBAIN GÉANT", 3f, 2.2f, 1f),
            G("corner-ad-wrap", "cinematic", "room_boundary", "MÉDIA", "FAÇADE D'ANGLE", 2.5f, 2f, 1.2f),
            G("window-cascade", "cinematic", "window_display", "FAÇADE", "CASCADE DE FENÊTRES", 2.4f, 2.8f, 1f),
            G("building-scan-grid", "cinematic", "room_boundary", "SCAN", "GRILLE ARCHITECTURALE", 3f, 3f, 1.5f),
            G("energy-pipeline", "cinematic", "vehicle_fx", "ÉNERGIE", "CANALISATION LUMINEUSE", 1.6f, 1f, 3f),
            G("alley-steam", "cinematic", "particle_column", "ATMOSPHÈRE", "VAPEUR DE RUELLE", 1.5f, 1.8f, 1.5f),
            G("neon-rain", "cinematic", "particle_column", "PLUIE", "PRÉCIPITATION NÉON", 2f, 3f, 2f),
            G("data-cloud", "cinematic", "particle_column", "NUAGE", "DONNÉES VOLUMÉTRIQUES", 2.2f, 1.5f, 2.2f),
            G("light-sculpture", "cinematic", "giant_hologram", "SCULPTURE", "FORME CINÉTIQUE", 1.8f, 2.4f, 1.8f),
            G("energy-vortex", "cinematic", "portal_arch", "VORTEX", "PUITS D'ÉNERGIE", 2.2f, 2.5f, 2.2f),
            G("sky-ring", "cinematic", "logo_orbit", "ANNEAU", "STRUCTURE CÉLESTE", 3f, 3f, 3f),
            G("district-header", "cinematic", "neon_sign", "DISTRICT", "TITRE MONUMENTAL", 3f, 1.5f, 1f),
            G("city-level-marker", "cinematic", "street_totem", "SECTEUR", "IDENTIFIANT VERTICAL", 1.2f, 2.8f, 1.2f),
            G("holo-phonebooth", "cinematic", "portal_arch", "COMMUNICATION", "CABINE HOLOGRAPHIQUE", 1.2f, 2f, 1.2f),
            G("subway-entrance", "cinematic", "portal_arch", "MÉTRO", "ENTRÉE AUGMENTÉE", 1.8f, 2f, 1.4f),
            G("rooftop-garden", "cinematic", "particle_column", "JARDIN", "CANOPÉE SYNTHÉTIQUE", 2.5f, 1.5f, 2.5f),
            G("interactive-mural", "cinematic", "window_display", "FRESQUE", "MUR RÉACTIF", 2.5f, 2f, 1f),
            G("holo-fountain", "cinematic", "particle_column", "FONTAINE", "FLUX DE PARTICULES", 1.8f, 2f, 1.8f),
            G("portal-network", "cinematic", "portal_arch", "RÉSEAU", "PORTE INTERCONNECTÉE", 1.8f, 2.2f, 1.2f),
            G("floating-caption", "cinematic", "annotation", "ANNOTATION", "TEXTE LIBRE FLOTTANT", 1.2f, .8f, 1f),
            G("custom-logo-panel", "cinematic", "window_display", "LOGO", "IMAGE PERSONNALISÉE", 1.3f, 1.1f, 1f),
        };

        private static readonly List<Entry> AllEntries = Build();

        public static IReadOnlyList<Entry> Entries => AllEntries;

        public static Entry Find(string presetId) =>
            AllEntries.Find(entry =>
                string.Equals(entry.presetId, presetId, StringComparison.Ordinal));

        public static List<Entry> ForCategory(string category)
        {
            string clean = (category ?? string.Empty).Trim().ToLowerInvariant();
            return AllEntries.FindAll(entry => entry.categoryId == clean);
        }

        private static List<Entry> Build()
        {
            var result = new List<Entry>(
                Grammars.Length * Palettes.Length * Animations.Length);
            foreach (Grammar grammar in Grammars)
            foreach (string[] palette in Palettes)
            foreach (string animation in Animations)
            {
                string id =
                    grammar.Key + "-" + palette[2] + "-" + animation;
                result.Add(new Entry
                {
                    presetId = id,
                    categoryId = grammar.Category,
                    archetypeId = grammar.Key,
                    templateId = grammar.Template,
                    styleId = palette[2],
                    animationId = animation,
                    accentHex = palette[0],
                    secondaryHex = palette[1],
                    label = grammar.Label,
                    subtitle = grammar.Subtitle,
                    defaultScale = grammar.Scale,
                });
            }
            return result;
        }

        private static Grammar G(
            string key,
            string category,
            string template,
            string label,
            string subtitle,
            float x,
            float y,
            float z) =>
            new Grammar
            {
                Key = key,
                Category = category,
                Template = template,
                Label = label,
                Subtitle = subtitle,
                Scale = new Vector3(x, y, z),
            };
    }
}

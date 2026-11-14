using StudySuit.PdfReader.Models;

namespace StudySuit.PdfReader.Services;

public static class NcertLibraryCatalog
{
    public static readonly IReadOnlyList<NcertBookItem> AllBooks =
    [
        // ======================== CLASS 12 ========================
        new NcertBookItem
        {
            Code = "leph1",
            Title = "Physics Part-I",
            ShortTitle = "Physics I",
            ClassNumber = 12,
            Subject = "Physics",
            SubjectCategory = "Science",
            Description = "Electrostatics, Current Electricity, Magnetism, and Electromagnetic Induction.",
            BadgeColorHex = "#7C3AED",
            EstimatedSizeDisplay = "~38 MB"
        },
        new NcertBookItem
        {
            Code = "leph2",
            Title = "Physics Part-II",
            ShortTitle = "Physics II",
            ClassNumber = 12,
            Subject = "Physics",
            SubjectCategory = "Science",
            Description = "Ray & Wave Optics, Dual Nature of Radiation, Atoms, Nuclei, and Semiconductors.",
            BadgeColorHex = "#7C3AED",
            EstimatedSizeDisplay = "~35 MB"
        },
        new NcertBookItem
        {
            Code = "lech1",
            Title = "Chemistry Part-I",
            ShortTitle = "Chemistry I",
            ClassNumber = 12,
            Subject = "Chemistry",
            SubjectCategory = "Science",
            Description = "Solutions, Electrochemistry, Chemical Kinetics, and Coordination Compounds.",
            BadgeColorHex = "#D97706",
            EstimatedSizeDisplay = "~42 MB"
        },
        new NcertBookItem
        {
            Code = "lech2",
            Title = "Chemistry Part-II",
            ShortTitle = "Chemistry II",
            ClassNumber = 12,
            Subject = "Chemistry",
            SubjectCategory = "Science",
            Description = "Haloalkanes, Alcohols, Aldehydes, Amines, and Biomolecules.",
            BadgeColorHex = "#D97706",
            EstimatedSizeDisplay = "~40 MB"
        },
        new NcertBookItem
        {
            Code = "lebo1",
            Title = "Biology",
            ShortTitle = "Biology",
            ClassNumber = 12,
            Subject = "Biology",
            SubjectCategory = "Science",
            Description = "Reproduction, Genetics, Evolution, Biotechnology, and Ecology.",
            BadgeColorHex = "#059669",
            EstimatedSizeDisplay = "~46 MB"
        },
        new NcertBookItem
        {
            Code = "lemh1",
            Title = "Mathematics Part-I",
            ShortTitle = "Mathematics I",
            ClassNumber = 12,
            Subject = "Mathematics",
            SubjectCategory = "Mathematics",
            Description = "Relations & Functions, Inverse Trig, Matrices, Determinants, and Calculus.",
            BadgeColorHex = "#2563EB",
            EstimatedSizeDisplay = "~32 MB"
        },
        new NcertBookItem
        {
            Code = "lemh2",
            Title = "Mathematics Part-II",
            ShortTitle = "Mathematics II",
            ClassNumber = 12,
            Subject = "Mathematics",
            SubjectCategory = "Mathematics",
            Description = "Integrals, Differential Equations, Vector Algebra, 3D Geometry, and Probability.",
            BadgeColorHex = "#2563EB",
            EstimatedSizeDisplay = "~28 MB"
        },
        new NcertBookItem
        {
            Code = "lehs1",
            Title = "Themes in Indian History Part-I",
            ShortTitle = "History I",
            ClassNumber = 12,
            Subject = "History",
            SubjectCategory = "Social Science",
            Description = "Harappan Civilization, Early States & Economies, Kinship, Caste & Class.",
            BadgeColorHex = "#DC2626",
            EstimatedSizeDisplay = "~24 MB"
        },
        new NcertBookItem
        {
            Code = "lehs2",
            Title = "Themes in Indian History Part-II",
            ShortTitle = "History II",
            ClassNumber = 12,
            Subject = "History",
            SubjectCategory = "Social Science",
            Description = "Bhakti-Sufi Traditions, Vijayanagara Empire, Agrarian Society, and Mughal Court.",
            BadgeColorHex = "#DC2626",
            EstimatedSizeDisplay = "~26 MB"
        },
        new NcertBookItem
        {
            Code = "lehs3",
            Title = "Themes in Indian History Part-III",
            ShortTitle = "History III",
            ClassNumber = 12,
            Subject = "History",
            SubjectCategory = "Social Science",
            Description = "Colonialism, 1857 Revolt, Mahatma Gandhi & National Movement, and Framing the Constitution.",
            BadgeColorHex = "#DC2626",
            EstimatedSizeDisplay = "~28 MB"
        },
        new NcertBookItem
        {
            Code = "legy1",
            Title = "Fundamentals of Human Geography",
            ShortTitle = "Human Geography",
            ClassNumber = 12,
            Subject = "Geography",
            SubjectCategory = "Social Science",
            Description = "World Population, Human Development, Primary/Secondary Activities, and Transport.",
            BadgeColorHex = "#0284C7",
            EstimatedSizeDisplay = "~22 MB"
        },
        new NcertBookItem
        {
            Code = "legy2",
            Title = "India: People and Economy",
            ShortTitle = "India Geography",
            ClassNumber = 12,
            Subject = "Geography",
            SubjectCategory = "Social Science",
            Description = "Population Distribution, Land Resources, Water, Minerals, and Planning in India.",
            BadgeColorHex = "#0284C7",
            EstimatedSizeDisplay = "~25 MB"
        },
        new NcertBookItem
        {
            Code = "leps1",
            Title = "Contemporary World Politics",
            ShortTitle = "World Politics",
            ClassNumber = 12,
            Subject = "Political Science",
            SubjectCategory = "Social Science",
            Description = "End of Bipolarity, Contemporary Centers of Power, South Asia, UN, and Globalization.",
            BadgeColorHex = "#9333EA",
            EstimatedSizeDisplay = "~20 MB"
        },
        new NcertBookItem
        {
            Code = "leps2",
            Title = "Politics in India Since Independence",
            ShortTitle = "Polity India",
            ClassNumber = 12,
            Subject = "Political Science",
            SubjectCategory = "Social Science",
            Description = "Nation Building, Era of One-Party Dominance, Democratic Order, and Regional Aspirations.",
            BadgeColorHex = "#9333EA",
            EstimatedSizeDisplay = "~24 MB"
        },
        new NcertBookItem
        {
            Code = "leec1",
            Title = "Introductory Microeconomics",
            ShortTitle = "Microeconomics",
            ClassNumber = 12,
            Subject = "Economics",
            SubjectCategory = "Social Science",
            Description = "Consumer Behaviour, Production, Costs, and Market Equilibrium.",
            BadgeColorHex = "#0D9488",
            EstimatedSizeDisplay = "~18 MB"
        },
        new NcertBookItem
        {
            Code = "leec2",
            Title = "Introductory Macroeconomics",
            ShortTitle = "Macroeconomics",
            ClassNumber = 12,
            Subject = "Economics",
            SubjectCategory = "Social Science",
            Description = "National Income Accounting, Money and Banking, Government Budget, and Open Economy.",
            BadgeColorHex = "#0D9488",
            EstimatedSizeDisplay = "~20 MB"
        },
        new NcertBookItem
        {
            Code = "lefl1",
            Title = "Flamingo (English Core)",
            ShortTitle = "Flamingo",
            ClassNumber = 12,
            Subject = "English",
            SubjectCategory = "Language",
            Description = "Prose and poetry selections for CBSE & competitive exams English prep.",
            BadgeColorHex = "#E11D48",
            EstimatedSizeDisplay = "~15 MB"
        },

        // ======================== CLASS 11 ========================
        new NcertBookItem
        {
            Code = "keph1",
            Title = "Physics Part-I",
            ShortTitle = "Physics I",
            ClassNumber = 11,
            Subject = "Physics",
            SubjectCategory = "Science",
            Description = "Units, Kinematics, Laws of Motion, Work Energy Power, and Gravitation.",
            BadgeColorHex = "#7C3AED",
            EstimatedSizeDisplay = "~36 MB"
        },
        new NcertBookItem
        {
            Code = "keph2",
            Title = "Physics Part-II",
            ShortTitle = "Physics II",
            ClassNumber = 11,
            Subject = "Physics",
            SubjectCategory = "Science",
            Description = "Mechanical Properties of Solids/Fluids, Thermodynamics, and Waves & Oscillations.",
            BadgeColorHex = "#7C3AED",
            EstimatedSizeDisplay = "~34 MB"
        },
        new NcertBookItem
        {
            Code = "kech1",
            Title = "Chemistry Part-I",
            ShortTitle = "Chemistry I",
            ClassNumber = 11,
            Subject = "Chemistry",
            SubjectCategory = "Science",
            Description = "Structure of Atom, Periodic Classification, Chemical Bonding, and Thermodynamics.",
            BadgeColorHex = "#D97706",
            EstimatedSizeDisplay = "~38 MB"
        },
        new NcertBookItem
        {
            Code = "kech2",
            Title = "Chemistry Part-II",
            ShortTitle = "Chemistry II",
            ClassNumber = 11,
            Subject = "Chemistry",
            SubjectCategory = "Science",
            Description = "Equilibrium, Redox Reactions, Organic Chemistry Basics, and Hydrocarbons.",
            BadgeColorHex = "#D97706",
            EstimatedSizeDisplay = "~36 MB"
        },
        new NcertBookItem
        {
            Code = "kebo1",
            Title = "Biology",
            ShortTitle = "Biology",
            ClassNumber = 11,
            Subject = "Biology",
            SubjectCategory = "Science",
            Description = "Living World, Cell Biology, Plant Physiology, and Human Physiology.",
            BadgeColorHex = "#059669",
            EstimatedSizeDisplay = "~52 MB"
        },
        new NcertBookItem
        {
            Code = "kemh1",
            Title = "Mathematics",
            ShortTitle = "Mathematics",
            ClassNumber = 11,
            Subject = "Mathematics",
            SubjectCategory = "Mathematics",
            Description = "Sets, Complex Numbers, Permutations, Binomial Theorem, and Conic Sections.",
            BadgeColorHex = "#2563EB",
            EstimatedSizeDisplay = "~35 MB"
        },
        new NcertBookItem
        {
            Code = "kehs1",
            Title = "Themes in World History",
            ShortTitle = "World History",
            ClassNumber = 11,
            Subject = "History",
            SubjectCategory = "Social Science",
            Description = "Early Empires, Nomadic Empires, Changing Cultural Traditions, and Industrial Revolution.",
            BadgeColorHex = "#DC2626",
            EstimatedSizeDisplay = "~30 MB"
        },
        new NcertBookItem
        {
            Code = "kegy1",
            Title = "Fundamentals of Physical Geography",
            ShortTitle = "Physical Geography",
            ClassNumber = 11,
            Subject = "Geography",
            SubjectCategory = "Social Science",
            Description = "Earth's Origin, Landforms, Climate, Water, and Biodiversity.",
            BadgeColorHex = "#0284C7",
            EstimatedSizeDisplay = "~26 MB"
        },
        new NcertBookItem
        {
            Code = "kegy2",
            Title = "India: Physical Environment",
            ShortTitle = "India Environment",
            ClassNumber = 11,
            Subject = "Geography",
            SubjectCategory = "Social Science",
            Description = "Structure and Physiography, Drainage Systems, Climate, Soils, and Natural Vegetation.",
            BadgeColorHex = "#0284C7",
            EstimatedSizeDisplay = "~24 MB"
        },
        new NcertBookItem
        {
            Code = "keps1",
            Title = "Indian Constitution at Work",
            ShortTitle = "Constitution at Work",
            ClassNumber = 11,
            Subject = "Political Science",
            SubjectCategory = "Social Science",
            Description = "Fundamental Rights, Elections, Executive, Legislature, Judiciary, and Federalism.",
            BadgeColorHex = "#9333EA",
            EstimatedSizeDisplay = "~22 MB"
        },
        new NcertBookItem
        {
            Code = "keps2",
            Title = "Political Theory",
            ShortTitle = "Political Theory",
            ClassNumber = 11,
            Subject = "Political Science",
            SubjectCategory = "Social Science",
            Description = "Freedom, Equality, Social Justice, Rights, Citizenship, and Nationalism.",
            BadgeColorHex = "#9333EA",
            EstimatedSizeDisplay = "~18 MB"
        },
        new NcertBookItem
        {
            Code = "keec1",
            Title = "Indian Economic Development",
            ShortTitle = "Indian Economy",
            ClassNumber = 11,
            Subject = "Economics",
            SubjectCategory = "Social Science",
            Description = "Development Policies 1947–1990, Economic Reforms, Rural Development, and Sustainable Growth.",
            BadgeColorHex = "#0D9488",
            EstimatedSizeDisplay = "~20 MB"
        },

        // ======================== CLASS 10 ========================
        new NcertBookItem
        {
            Code = "jesc1",
            Title = "Science Class 10",
            ShortTitle = "Science",
            ClassNumber = 10,
            Subject = "Science",
            SubjectCategory = "Science",
            Description = "Chemical Reactions, Acids Bases, Life Processes, Control & Coordination, Light, and Electricity.",
            BadgeColorHex = "#059669",
            EstimatedSizeDisplay = "~71 MB"
        },
        new NcertBookItem
        {
            Code = "jemh1",
            Title = "Mathematics Class 10",
            ShortTitle = "Mathematics",
            ClassNumber = 10,
            Subject = "Mathematics",
            SubjectCategory = "Mathematics",
            Description = "Real Numbers, Polynomials, Quadratic Equations, Triangles, Trig, and Statistics.",
            BadgeColorHex = "#2563EB",
            EstimatedSizeDisplay = "~45 MB"
        },
        new NcertBookItem
        {
            Code = "jess1",
            Title = "India & Contemporary World II (History)",
            ShortTitle = "History",
            ClassNumber = 10,
            Subject = "History",
            SubjectCategory = "Social Science",
            Description = "Rise of Nationalism in Europe, Nationalism in India, Making of Global World, and Print Culture.",
            BadgeColorHex = "#DC2626",
            EstimatedSizeDisplay = "~32 MB"
        },
        new NcertBookItem
        {
            Code = "jess2",
            Title = "Contemporary India II (Geography)",
            ShortTitle = "Geography",
            ClassNumber = 10,
            Subject = "Geography",
            SubjectCategory = "Social Science",
            Description = "Resources & Development, Forest & Wildlife, Water, Agriculture, Minerals, and Manufacturing.",
            BadgeColorHex = "#0284C7",
            EstimatedSizeDisplay = "~28 MB"
        },
        new NcertBookItem
        {
            Code = "jess3",
            Title = "Democratic Politics II (Civics)",
            ShortTitle = "Civics",
            ClassNumber = 10,
            Subject = "Political Science",
            SubjectCategory = "Social Science",
            Description = "Power Sharing, Federalism, Gender Religion & Caste, Political Parties, and Outcomes of Democracy.",
            BadgeColorHex = "#9333EA",
            EstimatedSizeDisplay = "~25 MB"
        },
        new NcertBookItem
        {
            Code = "jess4",
            Title = "Understanding Economic Development",
            ShortTitle = "Economics",
            ClassNumber = 10,
            Subject = "Economics",
            SubjectCategory = "Social Science",
            Description = "Development, Sectors of Indian Economy, Money & Credit, Globalization, and Consumer Rights.",
            BadgeColorHex = "#0D9488",
            EstimatedSizeDisplay = "~22 MB"
        },
        new NcertBookItem
        {
            Code = "jeff1",
            Title = "First Flight (English)",
            ShortTitle = "First Flight",
            ClassNumber = 10,
            Subject = "English",
            SubjectCategory = "Language",
            Description = "Main English prose and poetry reader for CBSE Class 10 board preparation.",
            BadgeColorHex = "#E11D48",
            EstimatedSizeDisplay = "~20 MB"
        },

        // ======================== CLASS 9 ========================
        new NcertBookItem
        {
            Code = "iesc1",
            Title = "Science Class 9",
            ShortTitle = "Science",
            ClassNumber = 9,
            Subject = "Science",
            SubjectCategory = "Science",
            Description = "Matter, Atoms & Molecules, Fundamental Unit of Life, Tissues, Motion, and Gravitation.",
            BadgeColorHex = "#059669",
            EstimatedSizeDisplay = "~62 MB"
        },
        new NcertBookItem
        {
            Code = "iemh1",
            Title = "Mathematics Class 9",
            ShortTitle = "Mathematics",
            ClassNumber = 9,
            Subject = "Mathematics",
            SubjectCategory = "Mathematics",
            Description = "Number Systems, Coordinate Geometry, Linear Equations, Euclid Geometry, and Circles.",
            BadgeColorHex = "#2563EB",
            EstimatedSizeDisplay = "~40 MB"
        },
        new NcertBookItem
        {
            Code = "iess1",
            Title = "India & Contemporary World I (History)",
            ShortTitle = "History",
            ClassNumber = 9,
            Subject = "History",
            SubjectCategory = "Social Science",
            Description = "French Revolution, Socialism in Europe & Russian Revolution, and Nazism.",
            BadgeColorHex = "#DC2626",
            EstimatedSizeDisplay = "~28 MB"
        },
        new NcertBookItem
        {
            Code = "iess2",
            Title = "Contemporary India I (Geography)",
            ShortTitle = "Geography",
            ClassNumber = 9,
            Subject = "Geography",
            SubjectCategory = "Social Science",
            Description = "India Size & Location, Physical Features, Drainage, Climate, and Natural Vegetation.",
            BadgeColorHex = "#0284C7",
            EstimatedSizeDisplay = "~26 MB"
        },
        new NcertBookItem
        {
            Code = "iess3",
            Title = "Democratic Politics I (Civics)",
            ShortTitle = "Civics",
            ClassNumber = 9,
            Subject = "Political Science",
            SubjectCategory = "Social Science",
            Description = "What is Democracy, Constitutional Design, Electoral Politics, and Democratic Rights.",
            BadgeColorHex = "#9333EA",
            EstimatedSizeDisplay = "~22 MB"
        },
        new NcertBookItem
        {
            Code = "iess4",
            Title = "Economics Class 9",
            ShortTitle = "Economics",
            ClassNumber = 9,
            Subject = "Economics",
            SubjectCategory = "Social Science",
            Description = "Story of Village Palampur, People as Resource, Poverty as Challenge, and Food Security.",
            BadgeColorHex = "#0D9488",
            EstimatedSizeDisplay = "~18 MB"
        },

        // ======================== CLASS 8 ========================
        new NcertBookItem
        {
            Code = "hesc1",
            Title = "Science Class 8",
            ShortTitle = "Science",
            ClassNumber = 8,
            Subject = "Science",
            SubjectCategory = "Science",
            Description = "Crop Production, Microorganisms, Combustion & Flame, Reproduction, and Force & Pressure.",
            BadgeColorHex = "#059669",
            EstimatedSizeDisplay = "~55 MB"
        },
        new NcertBookItem
        {
            Code = "hemh1",
            Title = "Mathematics Class 8",
            ShortTitle = "Mathematics",
            ClassNumber = 8,
            Subject = "Mathematics",
            SubjectCategory = "Mathematics",
            Description = "Rational Numbers, Linear Equations, Understanding Quadrilaterals, Algebraic Expressions, and Mensuration.",
            BadgeColorHex = "#2563EB",
            EstimatedSizeDisplay = "~38 MB"
        },
        new NcertBookItem
        {
            Code = "hess1",
            Title = "Our Pasts III (History)",
            ShortTitle = "History",
            ClassNumber = 8,
            Subject = "History",
            SubjectCategory = "Social Science",
            Description = "From Trade to Territory, Tribals Dikus & Golden Age, 1857 and After, and Making of National Movement.",
            BadgeColorHex = "#DC2626",
            EstimatedSizeDisplay = "~28 MB"
        },
        new NcertBookItem
        {
            Code = "hess2",
            Title = "Resource and Development (Geography)",
            ShortTitle = "Geography",
            ClassNumber = 8,
            Subject = "Geography",
            SubjectCategory = "Social Science",
            Description = "Resources, Land Soil Water Natural Vegetation, Agriculture, Industries, and Human Resources.",
            BadgeColorHex = "#0284C7",
            EstimatedSizeDisplay = "~24 MB"
        },
        new NcertBookItem
        {
            Code = "hess3",
            Title = "Social & Political Life III (Civics)",
            ShortTitle = "Civics",
            ClassNumber = 8,
            Subject = "Political Science",
            SubjectCategory = "Social Science",
            Description = "Indian Constitution, Secularism, Parliament & Law, Judiciary, and Marginalisation.",
            BadgeColorHex = "#9333EA",
            EstimatedSizeDisplay = "~22 MB"
        },

        // ======================== CLASS 7 ========================
        new NcertBookItem
        {
            Code = "gesc1",
            Title = "Science Class 7",
            ShortTitle = "Science",
            ClassNumber = 7,
            Subject = "Science",
            SubjectCategory = "Science",
            Description = "Nutrition in Plants & Animals, Heat, Acids Bases & Salts, Physical & Chemical Changes, and Respiration.",
            BadgeColorHex = "#059669",
            EstimatedSizeDisplay = "~48 MB"
        },
        new NcertBookItem
        {
            Code = "gemh1",
            Title = "Mathematics Class 7",
            ShortTitle = "Mathematics",
            ClassNumber = 7,
            Subject = "Mathematics",
            SubjectCategory = "Mathematics",
            Description = "Integers, Fractions & Decimals, Data Handling, Simple Equations, Lines & Angles, and Triangles.",
            BadgeColorHex = "#2563EB",
            EstimatedSizeDisplay = "~34 MB"
        },
        new NcertBookItem
        {
            Code = "gess1",
            Title = "Our Pasts II (History)",
            ShortTitle = "History",
            ClassNumber = 7,
            Subject = "History",
            SubjectCategory = "Social Science",
            Description = "Delhi Sultanate, Mughal Empire, Tribes Nomads, Devotional Paths, and 18th Century Formations.",
            BadgeColorHex = "#DC2626",
            EstimatedSizeDisplay = "~26 MB"
        },
        new NcertBookItem
        {
            Code = "gess2",
            Title = "Our Environment (Geography)",
            ShortTitle = "Geography",
            ClassNumber = 7,
            Subject = "Geography",
            SubjectCategory = "Social Science",
            Description = "Environment, Inside Our Earth, Our Changing Earth, Air, Water, and Human Interactions.",
            BadgeColorHex = "#0284C7",
            EstimatedSizeDisplay = "~22 MB"
        },

        // ======================== CLASS 6 ========================
        new NcertBookItem
        {
            Code = "fesc1",
            Title = "Curiosity: Science Class 6",
            ShortTitle = "Science",
            ClassNumber = 6,
            Subject = "Science",
            SubjectCategory = "Science",
            Description = "Exploring the Nature of Science, Food Components, Materials, Living World, and Motion.",
            BadgeColorHex = "#059669",
            EstimatedSizeDisplay = "~42 MB"
        },
        new NcertBookItem
        {
            Code = "femh1",
            Title = "Ganita Prakash: Mathematics Class 6",
            ShortTitle = "Mathematics",
            ClassNumber = 6,
            Subject = "Mathematics",
            SubjectCategory = "Mathematics",
            Description = "Knowing Our Numbers, Whole Numbers, Playing with Numbers, Basic Geometry, and Integers.",
            BadgeColorHex = "#2563EB",
            EstimatedSizeDisplay = "~32 MB"
        },
        new NcertBookItem
        {
            Code = "fess1",
            Title = "Exploring Society: India & Beyond",
            ShortTitle = "Social Science",
            ClassNumber = 6,
            Subject = "Social Science",
            SubjectCategory = "Social Science",
            Description = "Oceans and Continents, Landforms, India's Civilization, Early Kingdoms, and Grassroots Democracy.",
            BadgeColorHex = "#DC2626",
            EstimatedSizeDisplay = "~28 MB"
        },
        new NcertBookItem
        {
            Code = "feen1",
            Title = "Poorvi: English Class 6",
            ShortTitle = "Poorvi",
            ClassNumber = 6,
            Subject = "English",
            SubjectCategory = "Language",
            Description = "New curriculum foundational reader with rich moral stories, poems, and grammar foundations.",
            BadgeColorHex = "#E11D48",
            EstimatedSizeDisplay = "~18 MB"
        }
    ];
}

